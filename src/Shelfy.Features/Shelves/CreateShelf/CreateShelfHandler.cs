using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Shelves.CreateShelf;

public class CreateShelfHandler(ShelfyDbContext db)
{
    public async Task<HandlerResult<CreateShelfResponse>> HandleAsync(CreateShelfRequest request, CancellationToken ct = default)
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

        var shelf = new Shelf { Id = Guid.NewGuid(), UserId = request.UserId };
        db.Shelves.Add(shelf);
        await db.SaveChangesAsync(ct);

        return HandlerResult<CreateShelfResponse>.Ok(new CreateShelfResponse(shelf.Id, shelf.UserId, shelf.IsPublic));
    }
}
