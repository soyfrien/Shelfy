using System.Security.Claims;

namespace Shelfy.Features.Common;

public static class CurrentUser
{
    /// <summary>The signed-in user's id (account id = domain User id, ADR-012), or null for an anonymous caller.</summary>
    public static Guid? UserId(this ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true && Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;
}
