using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shelfy.Features.Common;

namespace Shelfy.Features.Shelves.CreateShelf;

public static class CreateShelfEndpoint
{
    public static IEndpointRouteBuilder MapCreateShelf(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/shelves", async (CreateShelfRequest request, System.Security.Claims.ClaimsPrincipal user, CreateShelfHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(request, ct, user.UserId()))
                    .ToHttpResult(shelf => Results.Created($"/api/shelves/{shelf.Id}", shelf)))
            .WithName("CreateShelf")
            .WithTags("Shelves")
            .Produces<CreateShelfResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
