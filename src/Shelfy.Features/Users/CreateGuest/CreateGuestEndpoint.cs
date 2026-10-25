using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shelfy.Features.Common;

namespace Shelfy.Features.Users.CreateGuest;

public static class CreateGuestEndpoint
{
    public static IEndpointRouteBuilder MapCreateGuest(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/guests", async (CreateGuestHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(ct)).ToHttpResult(g => Results.Created($"/api/guests/{g.Id}", g)))
            .WithName("CreateGuest")
            .WithTags("Users")
            .Produces<CreateGuestResponse>(StatusCodes.Status201Created);

        return app;
    }
}
