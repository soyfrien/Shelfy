using Microsoft.EntityFrameworkCore;
using Shelfy.Features.Common;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Shelves.HideShelf;

/// <summary>
/// Soft "clear shelf" (ADR-010): marks the shelf hidden. Its books, spines, jobs and photos are kept, but it is no longer
/// served (GET, /s/{id}, uploads, batch and job status all answer 404). Calling it again is fine.
/// </summary>
public class HideShelfHandler(ShelfyDbContext db, TimeProvider clock)
{
    // Who may call this is enforced by ShelfOwnerFilter on the endpoint: only the owner for account shelves, the link for guest shelves.
    public async Task<HandlerResult<bool>> HandleAsync(Guid shelfId, CancellationToken ct = default)
    {
        var shelf = await db.Shelves.FirstOrDefaultAsync(s => s.Id == shelfId, ct);
        if (shelf is null)
        {
            return HandlerResult<bool>.NotFound("Shelf not found.");
        }

        if (!shelf.IsHidden)
        {
            shelf.Hide(clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(ct);
        }

        return HandlerResult<bool>.Ok(true);
    }
}
