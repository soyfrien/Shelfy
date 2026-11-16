using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shelfy.Domain;
using Shelfy.Features.Lending.Copies;

namespace Shelfy.Features.Lending.CopyPage;

/// <summary>
/// /c/{code}: where a copy's QR sticker points (ADR-012). Serves wwwroot/copy.html (which loads the copy from
/// GET /api/copies/by-code/{code} and offers "I received this" to the right person), with link-preview tags filled in.
/// A code in a non-canonical form (lower case, dash, O for 0) redirects to the canonical URL; an unknown code gives 404.
/// </summary>
public static class CopyPageEndpoint
{
    public static IEndpointRouteBuilder MapCopyPage(this IEndpointRouteBuilder app)
    {
        app.MapGet("/c/{code}", async (string code, HttpContext http, CopiesHandler handler, IWebHostEnvironment env, CancellationToken ct) =>
            {
                var normalized = CopyCode.Normalize(code);
                if (normalized is not null && normalized != code)
                {
                    return Results.Redirect($"/c/{normalized}");
                }

                var copy = await handler.GetByCodeAsync(null, code, ct);
                var file = env.WebRootFileProvider.GetFileInfo("copy.html");
                if (!file.Exists)
                {
                    return Results.NotFound();
                }

                string template;
                await using (var stream = file.CreateReadStream())
                using (var reader = new StreamReader(stream))
                {
                    template = await reader.ReadToEndAsync(ct);
                }

                var title = copy.IsSuccess ? $"{copy.Value!.Title} · a Shelfy copy" : "Copy not found · Shelfy";
                var description = copy.IsSuccess
                    ? $"A copy of {copy.Value!.Title} by {copy.Value.Author}, from {copy.Value.OwnerName}'s shelf."
                    : "No book has this code.";
                var html = template
                    .Replace("{{TITLE}}", WebUtility.HtmlEncode(title))
                    .Replace("{{DESCRIPTION}}", WebUtility.HtmlEncode(description));
                http.Response.Headers.CacheControl = "no-cache";
                return Results.Content(html, "text/html; charset=utf-8", System.Text.Encoding.UTF8,
                    copy.IsSuccess ? StatusCodes.Status200OK : StatusCodes.Status404NotFound);
            })
            .WithName("CopyPage")
            .WithTags("Copies")
            .ExcludeFromDescription();

        return app;
    }
}
