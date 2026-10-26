using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Features.Recognition.Corrections;
using Shelfy.Features.Recognition.Parsing;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Recognition.FixSpine;

/// <summary>
/// "This spine is that book" in the tap-to-fix sheet (ADR-011). The book is upserted by ISBN-13 (a Book exists once), added
/// to the shelf as Private (never twice), the spine and every other unresolved read of the same text on the shelf are marked
/// resolved by the user, and the text is remembered (<see cref="UserCorrection"/>) so the pipeline matches it next time.
/// Idempotent: choosing the same book again answers 200 with nothing added; a different book for an already matched spine is a 409.
/// </summary>
public class ResolveSpineHandler(ShelfyDbContext db, TimeProvider clock)
{
    private const int MaxTitle = 500, MaxAuthor = 300, MaxCover = 2000;

    // TODO(ADR-006): only the shelf owner may fix a spine once authentication exists. Until then, like the rest of the API,
    // anyone who has the shelf id can. Book details sent by the client are only stored for a book that is new here.
    public async Task<HandlerResult<ResolveSpineResponse>> HandleAsync(Guid shelfId, Guid spineId, ResolveSpineRequest? request, CancellationToken ct = default)
    {
        if (request is null)
        {
            return HandlerResult<ResolveSpineResponse>.Invalid("A book is required: send isbn13 or bookId.");
        }

        string? isbn = null;
        if (request.BookId is null)
        {
            isbn = Isbn.Normalize(request.Isbn13);
            if (isbn is null)
            {
                return HandlerResult<ResolveSpineResponse>.Invalid("A valid isbn13 (or a bookId) is required.");
            }
        }

        var spine = await SpineFixSupport.FindSpineAsync(db, shelfId, spineId, track: true, ct);
        if (spine is null)
        {
            return HandlerResult<ResolveSpineResponse>.NotFound("Spine not found.");
        }

        var book = request.BookId is { } bookId
            ? await db.Books.FirstOrDefaultAsync(b => b.Id == bookId, ct)
            : await db.Books.FirstOrDefaultAsync(b => b.Isbn == isbn, ct);
        if (book is null && request.BookId is not null)
        {
            return HandlerResult<ResolveSpineResponse>.NotFound("Book not found.");
        }

        if (spine.BookId is { } current)
        {
            // Already matched: fine when it is the very same book (a double tap, a retry), otherwise it is not ours to overwrite.
            if (book is not null && book.Id == current)
            {
                var existing = await db.Books.AsNoTracking().FirstAsync(b => b.Id == current, ct);
                return HandlerResult<ResolveSpineResponse>.Ok(new ResolveSpineResponse(spineId, ToDto(existing), false, 0, false));
            }

            return HandlerResult<ResolveSpineResponse>.Conflict("This spine is already matched to a book.");
        }

        if (book is null)
        {
            var title = Truncate(request.Title?.Trim(), MaxTitle);
            if (string.IsNullOrEmpty(title))
            {
                return HandlerResult<ResolveSpineResponse>.Invalid("This book is not known yet: send its title and author too.");
            }

            var author = Truncate(request.Author?.Trim(), MaxAuthor);
            var cover = request.CoverUrl?.Trim();
            book = new Book
            {
                Id = Guid.NewGuid(),
                Isbn = isbn,
                Title = title,
                Author = string.IsNullOrEmpty(author) ? "Unknown" : author,
                CoverUrl = !string.IsNullOrEmpty(cover) && cover.Length <= MaxCover && Uri.TryCreate(cover, UriKind.Absolute, out var u) &&
                           u.Scheme is "http" or "https" ? cover : null
            };
            db.Books.Add(book);
        }

        var shelf = await db.Shelves.Include(s => s.Books).FirstAsync(s => s.Id == shelfId, ct);
        var added = shelf.AddBook(book.Id) is not null;

        var same = await SpineFixSupport.SameTextUnresolvedAsync(db, shelfId, spine, ct);
        foreach (var s in same)
        {
            s.MarkResolved(book, 1.0, DetectedSpine.SourceUser);
        }

        var remembered = await RememberAsync(spine, book, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two taps at once (or the pipeline adding the same book) lost the race on the unique book or shelf entry: ask again.
            db.ChangeTracker.Clear();
            return HandlerResult<ResolveSpineResponse>.Conflict("The shelf changed while saving. Please try again.");
        }

        return HandlerResult<ResolveSpineResponse>.Ok(new ResolveSpineResponse(spineId, ToDto(book), added, same.Count, remembered));
    }

    /// <summary>Stores the spine's text keys (whole text, parsed title) against the book; a changed mind replaces the old fix.</summary>
    private async Task<bool> RememberAsync(DetectedSpine spine, Book book, CancellationToken ct)
    {
        var keys = CorrectionKey.For(spine.RawText, spine.CandidateTitle);
        if (keys.Count == 0)
        {
            return false;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await db.UserCorrections.Where(c => keys.Contains(c.TextKey)).ToListAsync(ct);
        foreach (var key in keys)
        {
            var row = existing.FirstOrDefault(c => c.TextKey == key);
            if (row is null)
            {
                db.UserCorrections.Add(new UserCorrection { Id = Guid.NewGuid(), TextKey = key, BookId = book.Id, CreatedAt = now, UpdatedAt = now });
            }
            else if (row.BookId != book.Id)
            {
                row.BookId = book.Id;
                row.UpdatedAt = now;
            }
        }

        return true;
    }

    private static ResolvedSpineBookDto ToDto(Book b) => new(b.Id, b.Title, b.Author, b.Isbn, b.CoverUrl);

    private static string? Truncate(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
