using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shelfy.Features.Common;

namespace Shelfy.Features.Recognition.FixSpine;

/// <summary>Tap to fix (ADR-011): suggestions, resolve and dismiss for one unmatched spine of a shelf. Open API, like the rest.</summary>
public static class FixSpineEndpoints
{
    public static IEndpointRouteBuilder MapFixSpine(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/shelves/{shelfId:guid}/spines/{spineId:guid}/suggestions",
                async (Guid shelfId, Guid spineId, string? q, int? take, SpineSuggestionsHandler handler, CancellationToken ct) =>
                    (await handler.HandleAsync(shelfId, spineId, q, take ?? (string.IsNullOrWhiteSpace(q) ? SpineSuggestionsHandler.DefaultTake : SpineSuggestionsHandler.SearchTake), ct)).ToHttpResult())
            .WithName("GetSpineSuggestions").WithTags("Recognition")
            .Produces<SpineSuggestionsResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/shelves/{shelfId:guid}/spines/{spineId:guid}/resolve",
                async (Guid shelfId, Guid spineId, ResolveSpineRequest? body, ResolveSpineHandler handler, CancellationToken ct) =>
                    (await handler.HandleAsync(shelfId, spineId, body, ct)).ToHttpResult())
            .AddEndpointFilter<ShelfOwnerFilter>()
            .WithName("ResolveSpine").WithTags("Recognition")
            .Produces<ResolveSpineResponse>().ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/shelves/{shelfId:guid}/spines/{spineId:guid}/dismiss",
                async (Guid shelfId, Guid spineId, DismissSpineHandler handler, CancellationToken ct) =>
                    (await handler.HandleAsync(shelfId, spineId, ct)).ToHttpResult())
            .AddEndpointFilter<ShelfOwnerFilter>()
            .WithName("DismissSpine").WithTags("Recognition")
            .Produces<DismissSpineResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
