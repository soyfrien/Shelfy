using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shelfy.Features.Common;

namespace Shelfy.Features.Recognition.GetShelfUnmatched;

public static class GetShelfUnmatchedEndpoint
{
    public static IEndpointRouteBuilder MapGetShelfUnmatched(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/shelves/{shelfId:guid}/unmatched",
                async (Guid shelfId, GetShelfUnmatchedHandler handler, CancellationToken ct) =>
                    (await handler.HandleAsync(shelfId, ct)).ToHttpResult())
            .WithName("GetShelfUnmatched")
            .WithTags("Recognition")
            .Produces<ShelfUnmatchedResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
