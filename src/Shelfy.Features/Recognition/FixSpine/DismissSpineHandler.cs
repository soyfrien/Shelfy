using Shelfy.Features.Common;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Recognition.FixSpine;

/// <summary>
/// "Not a book" in the tap-to-fix sheet (ADR-011): the spine, and every other unresolved read of the same text on the shelf,
/// is no longer listed as unmatched. The reads stay in the database. Calling it again changes nothing. A spine that is
/// already matched to a book cannot be dismissed (409).
/// </summary>
public class DismissSpineHandler(ShelfyDbContext db)
{
    // TODO(ADR-006): only the shelf owner may dismiss a spine once authentication exists.
    public async Task<HandlerResult<DismissSpineResponse>> HandleAsync(Guid shelfId, Guid spineId, CancellationToken ct = default)
    {
        var spine = await SpineFixSupport.FindSpineAsync(db, shelfId, spineId, track: true, ct);
        if (spine is null)
        {
            return HandlerResult<DismissSpineResponse>.NotFound("Spine not found.");
        }

        if (spine.BookId is not null)
        {
            return HandlerResult<DismissSpineResponse>.Conflict("This spine is already matched to a book.");
        }

        var changed = 0;
        foreach (var s in await SpineFixSupport.SameTextUnresolvedAsync(db, shelfId, spine, ct))
        {
            if (!s.Dismissed)
            {
                s.Dismissed = true;
                changed++;
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return HandlerResult<DismissSpineResponse>.Ok(new DismissSpineResponse(spineId, true, changed));
    }
}
