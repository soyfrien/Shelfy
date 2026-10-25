namespace Shelfy.Features.Shelves.CreateShelf;

// TODO(ADR-006): take the user from the authenticated principal once Entra External ID is wired up.
public record CreateShelfRequest(Guid UserId);
