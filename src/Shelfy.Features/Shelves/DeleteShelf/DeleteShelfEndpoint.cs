using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shelfy.Features.Common;

namespace Shelfy.Features.Shelves.DeleteShelf;

public static class DeleteShelfEndpoint
{
    public static IEndpointRouteBuilder MapDeleteShelf(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/api/shelves/{shelfId:guid}", async (Guid shelfId, DeleteShelfHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(shelfId, ct)).ToHttpResult(_ => Results.NoContent()))
            .AddEndpointFilter<ShelfOwnerFilter>()
            .WithName("DeleteShelf")
            .WithTags("Shelves")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }
}
