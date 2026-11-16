using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Shelfy.Features.Common;
using Shelfy.Features.Lending.Labels;

namespace Shelfy.Features.Lending.Copies;

public static class CopiesEndpoints
{
    public static IEndpointRouteBuilder MapCopies(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/shelves/{shelfId:guid}/books/{bookId:guid}/copies",
                async (Guid shelfId, Guid bookId, RegisterCopyRequest? request, ClaimsPrincipal user, CopiesHandler handler, CancellationToken ct) =>
                    (await handler.RegisterAsync(user.UserId(), shelfId, bookId, request ?? new RegisterCopyRequest(), ct))
                        .ToHttpResult(c => Results.Created($"/api/copies/{c.Id}", c)))
            .RequireAuthorization()
            .WithName("RegisterCopy")
            .WithTags("Copies")
            .Produces<CopyDetailResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/shelves/{shelfId:guid}/copies", async (Guid shelfId, ClaimsPrincipal user, CopiesHandler handler, CancellationToken ct) =>
                (await handler.ForShelfAsync(user.UserId(), shelfId, ct)).ToHttpResult())
            .WithName("GetShelfCopies")
            .WithTags("Copies")
            .Produces<IReadOnlyList<ShelfCopyDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/copies/{copyId:guid}", async (Guid copyId, ClaimsPrincipal user, CopiesHandler handler, CancellationToken ct) =>
                (await handler.GetAsync(user.UserId(), copyId, ct)).ToHttpResult())
            .WithName("GetCopy")
            .WithTags("Copies")
            .Produces<CopyDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/copies/by-code/{code}", async (string code, ClaimsPrincipal user, CopiesHandler handler, CancellationToken ct) =>
                (await handler.GetByCodeAsync(user.UserId(), code, ct)).ToHttpResult())
            .WithName("GetCopyByCode")
            .WithTags("Copies")
            .Produces<CopyDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPut("/api/copies/{copyId:guid}", async (Guid copyId, UpdateCopyRequest request, ClaimsPrincipal user, CopiesHandler handler, CancellationToken ct) =>
                (await handler.UpdateAsync(user.UserId(), copyId, request, ct)).ToHttpResult())
            .RequireAuthorization()
            .WithName("UpdateCopy")
            .WithTags("Copies")
            .Produces<CopyDetailResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Printable QR sticker. The code is printed on the book anyway, so the label is public.
        app.MapGet("/api/copies/{copyId:guid}/label.svg",
                async (Guid copyId, HttpContext http, IConfiguration config, CopiesHandler handler, CancellationToken ct) =>
                {
                    var copy = await handler.GetAsync(null, copyId, ct);
                    if (!copy.IsSuccess)
                    {
                        return copy.ToHttpResult();
                    }

                    var origin = config["Lending:PublicBaseUrl"] is { Length: > 0 } configured ? configured : $"{http.Request.Scheme}://{http.Request.Host}";
                    var svg = CopyLabel.Svg(CopyLabel.Url(origin, copy.Value!.Code), copy.Value.Code, copy.Value.Title);
                    return Results.Content(svg, "image/svg+xml; charset=utf-8");
                })
            .WithName("GetCopyLabel")
            .WithTags("Copies")
            .Produces(StatusCodes.Status200OK, contentType: "image/svg+xml")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
