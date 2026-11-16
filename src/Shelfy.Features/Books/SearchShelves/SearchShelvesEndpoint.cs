using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shelfy.Features.Common;

namespace Shelfy.Features.Books.SearchShelves;

public static class SearchShelvesEndpoint
{
    public static IEndpointRouteBuilder MapSearchShelves(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/search", async (string? q, int? limit, int? offset, SearchShelvesHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new SearchShelvesRequest(q, limit ?? SearchShelvesHandler.DefaultLimit, offset ?? 0), ct)).ToHttpResult())
            .WithName("SearchShelves")
            .WithTags("Search")
            .Produces<SearchShelvesResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}
