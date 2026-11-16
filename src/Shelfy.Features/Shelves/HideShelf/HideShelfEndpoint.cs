using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shelfy.Features.Common;

namespace Shelfy.Features.Shelves.HideShelf;

public static class HideShelfEndpoint
{
    public static IEndpointRouteBuilder MapHideShelf(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/shelves/{shelfId:guid}/hide", async (Guid shelfId, HideShelfHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(shelfId, ct)).ToHttpResult(_ => Results.NoContent()))
            .AddEndpointFilter<ShelfOwnerFilter>()
            .WithName("HideShelf")
            .WithTags("Shelves")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
