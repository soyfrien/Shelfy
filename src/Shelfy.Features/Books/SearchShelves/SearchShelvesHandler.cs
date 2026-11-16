using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Books.SearchShelves;

/// <summary>
/// Cross-shelf catalog search: find a book on public shelves by title, author or ISBN (10 or 13).
/// Hidden and private shelves are never included, including guest "dark" shelves that have not been made public.
/// Searches <see cref="Book"/> rows first, then joins holdings. The Book model has no publisher or series field, so those
/// are not searchable.
/// </summary>
public class SearchShelvesHandler(ShelfyDbContext db)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;
    public const int MaxQueryLength = 200;
    public const int MinPartialIsbnDigits = 8;

    public async Task<HandlerResult<SearchShelvesResponse>> HandleAsync(SearchShelvesRequest request, CancellationToken ct = default)
    {
        var query = request.Query?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return HandlerResult<SearchShelvesResponse>.Invalid("Query is required.");
        }

        if (query.Length > MaxQueryLength)
        {
            return HandlerResult<SearchShelvesResponse>.Invalid($"Query must be at most {MaxQueryLength} characters.");
        }

        if (request.Limit is < 1 or > MaxLimit)
        {
            return HandlerResult<SearchShelvesResponse>.Invalid($"Limit must be between 1 and {MaxLimit}.");
        }

        if (request.Offset < 0)
        {
            return HandlerResult<SearchShelvesResponse>.Invalid("Offset cannot be negative.");
        }

        var term = query.ToLower();
        var exactIsbn = Isbn.Normalize(query) ?? Isbn.FindIn(query);
        var digits = Isbn.Digits(query);
        var partialIsbn = exactIsbn is null && digits.Length >= MinPartialIsbnDigits && digits.All(char.IsAsciiDigit)
            ? digits
            : null;

        // Only shelves that are opted into public sharing and have not been hidden. Guest shelves use the same rule:
        // they stay out of search until IsPublic is true (a link still opens a non-public guest shelf).
        var publicShelfIds = db.Shelves.AsNoTracking().Where(s => s.IsPublic && !s.IsHidden).Select(s => s.Id);
        var visibleBookIds = db.ShelfBooks.AsNoTracking()
            .Where(sb => publicShelfIds.Contains(sb.ShelfId))
            .Select(sb => sb.BookId);

        var matches = db.Books.AsNoTracking().Where(b => visibleBookIds.Contains(b.Id) && (
            (exactIsbn != null && b.Isbn13 == exactIsbn)
            || (partialIsbn != null && b.Isbn13 != null && b.Isbn13.Contains(partialIsbn))
            || b.Title.ToLower().Contains(term)
            || b.Author.ToLower().Contains(term)));

        var ranked = matches
            .Select(b => new
            {
                b.Id,
                b.Title,
                b.Author,
                b.Isbn,
                b.Isbn13,
                b.CoverUrl,
                Rank = exactIsbn != null && b.Isbn13 == exactIsbn ? 0
                    : b.Title.ToLower().Contains(term) ? 1
                    : 2
            })
            .OrderBy(b => b.Rank)
            .ThenBy(b => b.Title);

        var total = await ranked.CountAsync(ct);
        var page = await ranked.Skip(request.Offset).Take(request.Limit).ToListAsync(ct);
        if (page.Count == 0)
        {
            return HandlerResult<SearchShelvesResponse>.Ok(new SearchShelvesResponse(query, total, request.Limit, request.Offset, []));
        }

        var bookIds = page.Select(b => b.Id).ToList();
        var holdings = await db.ShelfBooks.AsNoTracking()
            .Where(sb => bookIds.Contains(sb.BookId) && publicShelfIds.Contains(sb.ShelfId))
            .Join(db.Shelves.AsNoTracking(), sb => sb.ShelfId, s => s.Id, (sb, s) => new { sb.BookId, Shelf = s })
            .ToListAsync(ct);

        var shelfIds = holdings.Select(h => h.Shelf.Id).Distinct().ToList();
        var ownerIds = holdings.Select(h => h.Shelf.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var accounts = await db.Accounts.AsNoTracking().Where(a => ownerIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);

        var copies = await db.Copies.AsNoTracking()
            .Where(c => bookIds.Contains(c.BookId) && c.HomeShelfId != null && shelfIds.Contains(c.HomeShelfId.Value))
            .Select(c => new { c.BookId, ShelfId = c.HomeShelfId!.Value, c.Code, c.Status })
            .ToListAsync(ct);

        var books = page.Select(b =>
        {
            var shelves = holdings.Where(h => h.BookId == b.Id)
                .Select(h =>
                {
                    var owner = users.GetValueOrDefault(h.Shelf.UserId);
                    accounts.TryGetValue(h.Shelf.UserId, out var account);
                    var shelfCopies = copies
                        .Where(c => c.BookId == b.Id && c.ShelfId == h.Shelf.Id)
                        .OrderBy(c => c.Code)
                        .Select(c => new SearchCopyDto(c.Code, c.Status.ToString()))
                        .ToList();
                    return new SearchShelfDto(
                        h.Shelf.Id,
                        PublicUserName.Of(owner, account),
                        shelfCopies.Any(c => c.Status == nameof(CopyStatus.Available)),
                        shelfCopies);
                })
                .OrderBy(s => s.OwnerName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Id)
                .ToList();
            return new SearchBookDto(b.Id, b.Title, b.Author, b.Isbn13 ?? b.Isbn, b.CoverUrl,
                shelves.Any(s => s.Available), shelves);
        }).ToList();

        return HandlerResult<SearchShelvesResponse>.Ok(new SearchShelvesResponse(query, total, request.Limit, request.Offset, books));
    }
}
