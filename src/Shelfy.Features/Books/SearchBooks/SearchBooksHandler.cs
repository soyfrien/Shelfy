using Microsoft.EntityFrameworkCore;
using Shelfy.Features.Common;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Books.SearchBooks;

public class SearchBooksHandler(ShelfyDbContext db)
{
    public const int MaxTake = 50;

    // Stub: case-insensitive substring match on title, author or ISBN.
    // ADR-001: replace with PostgreSQL full-text search (tsvector + GIN index).
    public async Task<HandlerResult<SearchBooksResponse>> HandleAsync(SearchBooksRequest request, CancellationToken ct = default)
    {
        var query = request.Query?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return HandlerResult<SearchBooksResponse>.Invalid("Query is required.");
        }

        if (request.Take is < 1 or > MaxTake)
        {
            return HandlerResult<SearchBooksResponse>.Invalid($"Take must be between 1 and {MaxTake}.");
        }

        var term = query.ToLower();
        var books = await db.Books
            .AsNoTracking()
            .Where(b => b.Title.ToLower().Contains(term)
                     || b.Author.ToLower().Contains(term)
                     || (b.Isbn != null && b.Isbn.Contains(term)))
            .OrderBy(b => b.Title)
            .Take(request.Take)
            .Select(b => new BookSummary(b.Id, b.Title, b.Author, b.Isbn, b.CoverUrl))
            .ToListAsync(ct);

        return HandlerResult<SearchBooksResponse>.Ok(new SearchBooksResponse(books));
    }
}
