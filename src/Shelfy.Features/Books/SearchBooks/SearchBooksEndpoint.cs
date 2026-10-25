using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shelfy.Features.Common;

namespace Shelfy.Features.Books.SearchBooks;

public static class SearchBooksEndpoint
{
    public static IEndpointRouteBuilder MapSearchBooks(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/books/search", async (string q, int? take, SearchBooksHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new SearchBooksRequest(q, take ?? 20), ct)).ToHttpResult())
            .WithName("SearchBooks")
            .WithTags("Books")
            .Produces<SearchBooksResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}
