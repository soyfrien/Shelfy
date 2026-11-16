using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Common;

/// <summary>
/// Protects shelves that belong to a registered account (ADR-012): changing such a shelf (upload, fix, hide, delete) needs the
/// owner to be signed in. Anonymous guest shelves stay open exactly as before (ADR-006/ADR-010). Reads stay public.
/// Reads the shelf id from the route value "shelfId"; unknown shelves pass through so the handler answers 404 as before.
/// </summary>
public class ShelfOwnerFilter(ShelfyDbContext db) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (Guid.TryParse(http.Request.RouteValues["shelfId"]?.ToString(), out var shelfId))
        {
            var ownerId = await db.Shelves.AsNoTracking().Where(s => s.Id == shelfId).Select(s => (Guid?)s.UserId)
                .FirstOrDefaultAsync(http.RequestAborted);
            if (ownerId is not null && await db.Accounts.AnyAsync(a => a.Id == ownerId, http.RequestAborted))
            {
                var caller = http.User.UserId();
                if (caller is null)
                {
                    return Results.Json(new { error = "Sign in to change this shelf." }, statusCode: StatusCodes.Status401Unauthorized);
                }

                if (caller != ownerId)
                {
                    return Results.Json(new { error = "Only the owner can change this shelf." }, statusCode: StatusCodes.Status403Forbidden);
                }
            }
        }

        return await next(context);
    }
}
