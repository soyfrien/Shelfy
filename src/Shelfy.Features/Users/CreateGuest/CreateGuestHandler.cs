using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Users.CreateGuest;

/// <summary>
/// Creates an anonymous guest user so the web UI can own a shelf before sign-in exists.
/// TODO(ADR-006): remove once Entra External ID is wired up; users will then be created from the authenticated principal.
/// </summary>
public class CreateGuestHandler(ShelfyDbContext db)
{
    public async Task<HandlerResult<CreateGuestResponse>> HandleAsync(CancellationToken ct = default)
    {
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Guest" };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return HandlerResult<CreateGuestResponse>.Ok(new CreateGuestResponse(user.Id, user.DisplayName));
    }
}
