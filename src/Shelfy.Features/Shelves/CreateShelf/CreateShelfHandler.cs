using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Shelves.CreateShelf;

public class CreateShelfHandler(ShelfyDbContext db)
{
    /// <param name="callerId">The signed-in user, if any. A shelf for a registered account can only be made by that account (ADR-012);
    /// anonymous guests keep working without sign-in.</param>
    public async Task<HandlerResult<CreateShelfResponse>> HandleAsync(CreateShelfRequest request, CancellationToken ct = default, Guid? callerId = null)
    {
        if (request.UserId == Guid.Empty)
        {
            return HandlerResult<CreateShelfResponse>.Invalid("UserId is required.");
        }

        var userExists = await db.Users.AnyAsync(u => u.Id == request.UserId, ct);
        if (!userExists)
        {
            return HandlerResult<CreateShelfResponse>.NotFound("User not found.");
        }

        if (callerId != request.UserId && await db.Accounts.AnyAsync(a => a.Id == request.UserId, ct))
        {
            return HandlerResult<CreateShelfResponse>.Forbidden("Sign in as this user to create a shelf for them.");
        }

        var shelf = new Shelf { Id = Guid.NewGuid(), UserId = request.UserId };
        db.Shelves.Add(shelf);
        await db.SaveChangesAsync(ct);

        return HandlerResult<CreateShelfResponse>.Ok(new CreateShelfResponse(shelf.Id, shelf.UserId, shelf.IsPublic));
    }
}
