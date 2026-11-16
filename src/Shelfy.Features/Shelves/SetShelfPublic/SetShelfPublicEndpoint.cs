using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shelfy.Features.Common;

namespace Shelfy.Features.Shelves.SetShelfPublic;

public static class SetShelfPublicEndpoint
{
    public static IEndpointRouteBuilder MapSetShelfPublic(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/api/shelves/{shelfId:guid}", async (Guid shelfId, SetShelfPublicRequest request, ClaimsPrincipal user, SetShelfPublicHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(shelfId, request, user.UserId(), ct)).ToHttpResult())
            .AddEndpointFilter<ShelfOwnerFilter>()
            .WithName("SetShelfPublic")
            .WithTags("Shelves")
            .Produces<SetShelfPublicResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
