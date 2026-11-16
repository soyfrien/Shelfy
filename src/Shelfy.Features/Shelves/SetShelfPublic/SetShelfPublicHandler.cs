using Microsoft.EntityFrameworkCore;
using Shelfy.Features.Common;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Shelves.SetShelfPublic;

/// <summary>
/// Lets the signed-in owner of an account-owned shelf show or hide it in cross-shelf search (IsPublic).
/// Guest (dark) shelves cannot be toggled this way. A private shelf can still be opened by its link.
/// </summary>
public class SetShelfPublicHandler(ShelfyDbContext db)
{
    public async Task<HandlerResult<SetShelfPublicResponse>> HandleAsync(
        Guid shelfId, SetShelfPublicRequest request, Guid? callerId, CancellationToken ct = default)
    {
        if (callerId is null)
        {
            return HandlerResult<SetShelfPublicResponse>.Unauthorized("Sign in to change this shelf.");
        }

        var shelf = await db.Shelves.FirstOrDefaultAsync(s => s.Id == shelfId, ct);
        if (shelf is null || shelf.IsHidden)
        {
            return HandlerResult<SetShelfPublicResponse>.NotFound("Shelf not found.");
        }

        if (!await db.Accounts.AnyAsync(a => a.Id == shelf.UserId, ct))
        {
            return HandlerResult<SetShelfPublicResponse>.Forbidden("Guest shelves can't be shown in search.");
        }

        if (callerId != shelf.UserId)
        {
            return HandlerResult<SetShelfPublicResponse>.Forbidden("Only the owner can change this shelf.");
        }

        if (shelf.IsPublic != request.IsPublic)
        {
            shelf.SetPublic(request.IsPublic);
            await db.SaveChangesAsync(ct);
        }

        return HandlerResult<SetShelfPublicResponse>.Ok(new SetShelfPublicResponse(shelf.Id, shelf.IsPublic));
    }
}
