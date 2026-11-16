using Microsoft.AspNetCore.Identity;

namespace Shelfy.Infrastructure;

/// <summary>
/// Sign-in credentials (ASP.NET Core Identity, email + password; ADR-012). The account's Id is the Id of its domain
/// <see cref="Shelfy.Domain.User"/> row (one-to-one), so shelves, copies and loans keep pointing at Users.
/// </summary>
public class ShelfyAccount : IdentityUser<Guid>
{
    public DateTime CreatedAt { get; set; }
}
