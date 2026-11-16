using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Lending.Copies;

/// <summary>
/// Physical copies (ADR-012): register a book on your shelf as a lendable copy, list the copies on a shelf, read one copy
/// (by id or by its sticker code) with its journey, and change its lending settings.
/// </summary>
public class CopiesHandler(ShelfyDbContext db, ICopyCodeGenerator codes, TimeProvider clock)
{
    public const int MaxCodeAttempts = 5;

    /// <summary>
    /// The owner of a shelf turns a book on it (recognized from a photo or fixed by hand) into a lendable copy. Books stay
    /// Private until their owner does this (docs/domain.md), so lending is opt-in per physical book.
    /// </summary>
    public async Task<HandlerResult<CopyDetailResponse>> RegisterAsync(Guid? me, Guid shelfId, Guid bookId, RegisterCopyRequest request, CancellationToken ct = default)
    {
        if (me is null)
        {
            return HandlerResult<CopyDetailResponse>.Unauthorized("Sign in to lend your books.");
        }

        var shelf = await db.Shelves.Include(s => s.Books).FirstOrDefaultAsync(s => s.Id == shelfId && !s.IsHidden, ct);
        if (shelf is null)
        {
            return HandlerResult<CopyDetailResponse>.NotFound("Shelf not found.");
        }

        if (shelf.UserId != me)
        {
            return HandlerResult<CopyDetailResponse>.Forbidden("Only the owner of this shelf can lend its books.");
        }

        var shelfBook = shelf.Books.FirstOrDefault(b => b.BookId == bookId);
        if (shelfBook is null)
        {
            return HandlerResult<CopyDetailResponse>.NotFound("That book is not on this shelf.");
        }

        if (request.Condition?.Length > 100 || request.Notes?.Length > 1000)
        {
            return HandlerResult<CopyDetailResponse>.Invalid("Condition can be at most 100 and notes at most 1000 characters.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        for (var attempt = 1; ; attempt++)
        {
            var code = codes.Next();
            if (await db.Copies.AnyAsync(c => c.Code == code, ct))
            {
                if (attempt >= MaxCodeAttempts)
                {
                    return HandlerResult<CopyDetailResponse>.Conflict("Could not allocate a copy code. Please try again.");
                }

                continue;
            }

            var copy = Copy.Register(bookId, me.Value, shelfId, code, now);
            copy.AllowPassAlong = request.AllowPassAlong;
            copy.Condition = Clean(request.Condition);
            copy.Notes = Clean(request.Notes);
            db.Copies.Add(copy);
            db.CopyMovements.Add(new CopyMovement
            {
                Id = Guid.NewGuid(), CopyId = copy.Id, Kind = CopyMovementKind.Registered, ToUserId = me.Value,
                ToShelfId = shelfId, StartedAt = now, ReceivedAt = now
            });
            if (shelfBook.Status == AvailabilityStatus.Private)
            {
                shelfBook.OfferForLoan();
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (attempt < MaxCodeAttempts)
            {
                // Lost a race for the same code (unique index): forget the attempt and draw another one.
                db.ChangeTracker.Clear();
                shelf = await db.Shelves.Include(s => s.Books).FirstAsync(s => s.Id == shelfId, ct);
                shelfBook = shelf.Books.First(b => b.BookId == bookId);
                continue;
            }

            return await GetAsync(me, copy.Id, ct);
        }
    }

    public async Task<HandlerResult<IReadOnlyList<ShelfCopyDto>>> ForShelfAsync(Guid? me, Guid shelfId, CancellationToken ct = default)
    {
        if (!await db.Shelves.AnyAsync(s => s.Id == shelfId && !s.IsHidden, ct))
        {
            return HandlerResult<IReadOnlyList<ShelfCopyDto>>.NotFound("Shelf not found.");
        }

        var copies = await db.Copies.AsNoTracking().Include(c => c.Book)
            .Where(c => c.HomeShelfId == shelfId || c.CurrentShelfId == shelfId)
            .ToListAsync(ct);
        var ids = copies.Select(c => c.Id).ToList();
        var loans = await db.Loans.AsNoTracking().Where(l => ids.Contains(l.CopyId)).WhereOpen().ToListAsync(ct);
        var users = await LendingQueries.UsersAsync(db, copies.Select(c => (Guid?)c.OwnerId), ct);

        var list = copies
            .OrderBy(c => c.Book!.Title).ThenBy(c => c.CreatedAt)
            .Select(c =>
            {
                var mine = loans.Where(l => l.CopyId == c.Id).ToList();
                return new ShelfCopyDto(c.Id, c.Code, c.BookId, c.Book!.Title, c.Book.Author, c.Book.CoverUrl, c.Status,
                    users.Name(c.OwnerId), c.HomeShelfId == shelfId, c.HomeShelfId != shelfId && c.CurrentShelfId == shelfId,
                    mine.Count(l => l.Status == LoanStatus.Waitlisted), LendingQueries.Viewer(c, mine, me));
            })
            .ToList();
        return HandlerResult<IReadOnlyList<ShelfCopyDto>>.Ok(list);
    }

    public async Task<HandlerResult<CopyDetailResponse>> GetByCodeAsync(Guid? me, string code, CancellationToken ct = default)
    {
        var normalized = CopyCode.Normalize(code);
        var id = normalized is null ? null : await db.Copies.Where(c => c.Code == normalized).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        return id is null ? HandlerResult<CopyDetailResponse>.NotFound("No copy has that code.") : await GetAsync(me, id.Value, ct);
    }

    public async Task<HandlerResult<CopyDetailResponse>> GetAsync(Guid? me, Guid copyId, CancellationToken ct = default)
    {
        var agg = await LendingQueries.LoadAsync(db, copyId, ct);
        if (agg is null)
        {
            return HandlerResult<CopyDetailResponse>.NotFound("Copy not found.");
        }

        var (copy, loans, movements) = agg;
        var users = await LendingQueries.UsersAsync(db,
            movements.SelectMany(m => new[] { m.FromUserId, (Guid?)m.ToUserId }).Append(copy.OwnerId).Append(copy.CurrentHolderId), ct);
        var history = movements
            .OrderBy(m => m.StartedAt)
            .Select(m => new MovementDto(m.Kind, m.FromUserId is null ? null : users.Name(m.FromUserId), users.Name(m.ToUserId), m.StartedAt, m.ReceivedAt))
            .ToList();
        var open = loans.Where(l => l.IsOpen).ToList();
        var b = copy.Book!;
        return HandlerResult<CopyDetailResponse>.Ok(new CopyDetailResponse(
            copy.Id, copy.Code, CopyCode.Format(copy.Code), copy.BookId, b.Title, b.Author, b.Isbn, b.CoverUrl, copy.Status,
            users.Name(copy.OwnerId), copy.HomeShelfId, users.Name(copy.CurrentHolderId), copy.AllowPassAlong, copy.Condition,
            copy.Notes, open.Count(l => l.Status == LoanStatus.Waitlisted), copy.CreatedAt, history,
            LendingQueries.Viewer(copy, open, me)));
    }

    public async Task<HandlerResult<CopyDetailResponse>> UpdateAsync(Guid? me, Guid copyId, UpdateCopyRequest request, CancellationToken ct = default)
    {
        if (me is null)
        {
            return HandlerResult<CopyDetailResponse>.Unauthorized("Sign in first.");
        }

        var agg = await LendingQueries.LoadAsync(db, copyId, ct);
        if (agg is null)
        {
            return HandlerResult<CopyDetailResponse>.NotFound("Copy not found.");
        }

        var copy = agg.Copy;
        if (copy.OwnerId != me)
        {
            return HandlerResult<CopyDetailResponse>.Forbidden("Only the owner can change this copy.");
        }

        if (request.Condition?.Length > 100 || request.Notes?.Length > 1000)
        {
            return HandlerResult<CopyDetailResponse>.Invalid("Condition can be at most 100 and notes at most 1000 characters.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        try
        {
            if (request.Available is bool available && available != (copy.Status != CopyStatus.Unavailable))
            {
                Domain.Lending.SetAvailable(copy, agg.Loans, me.Value, available, now);
            }
        }
        catch (LendingException ex)
        {
            return HandlerResult<CopyDetailResponse>.From(ex);
        }

        if (request.AllowPassAlong is bool pass) copy.AllowPassAlong = pass;
        if (request.Condition is not null) copy.Condition = Clean(request.Condition);
        if (request.Notes is not null) copy.Notes = Clean(request.Notes);
        copy.UpdatedAt = now;
        copy.Version++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return HandlerResult<CopyDetailResponse>.Conflict("This copy was changed just now. Reload and try again.");
        }

        return await GetAsync(me, copyId, ct);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
