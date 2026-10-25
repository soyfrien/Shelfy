using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shelfy.Features.Common;

namespace Shelfy.Features.Shelves.GetShelf;

public static class GetShelfEndpoint
{
    public static IEndpointRouteBuilder MapGetShelf(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/shelves/{shelfId:guid}", async (Guid shelfId, GetShelfHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new GetShelfRequest(shelfId), ct)).ToHttpResult())
            .WithName("GetShelf")
            .WithTags("Shelves")
            .Produces<GetShelfResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
