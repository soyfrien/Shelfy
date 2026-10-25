using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shelfy.Features.Common;
using Shelfy.Features.Shelves.GetShelf;

namespace Shelfy.Features.Shelves.ShelfPage;

/// <summary>
/// The shareable shelf page at /s/{shelfId}. The HTML template lives in wwwroot/shelf.html (the page itself loads the books
/// from GET /api/shelves/{id}); the server only fills in the link-preview tags (title, description, image) and returns a
/// 404 page for an unknown shelf.
/// </summary>
public static class ShelfPageEndpoint
{
    public static IEndpointRouteBuilder MapShelfPage(this IEndpointRouteBuilder app)
    {
        app.MapGet("/s/{shelfId:guid}", async (Guid shelfId, HttpContext http, GetShelfHandler handler, IWebHostEnvironment env, CancellationToken ct) =>
            {
                var shelf = await handler.HandleAsync(new GetShelfRequest(shelfId), ct);
                var template = await ReadTemplateAsync(env, shelf.IsSuccess ? "shelf.html" : "shelf-not-found.html", ct);
                if (template is null)
                {
                    return Results.NotFound();
                }

                var status = shelf.IsSuccess ? StatusCodes.Status200OK : StatusCodes.Status404NotFound;
                var html = shelf.IsSuccess ? Fill(template, shelf.Value!, $"{http.Request.Scheme}://{http.Request.Host}") : template;
                http.Response.Headers.CacheControl = "no-cache";
                return Results.Content(html, "text/html; charset=utf-8", System.Text.Encoding.UTF8, status);
            })
            .WithName("ShelfPage")
            .WithTags("Shelves")
            .ExcludeFromDescription();

        return app;
    }

    /// <summary>Fills the link-preview placeholders. Everything inserted is HTML-encoded.</summary>
    public static string Fill(string template, GetShelfResponse shelf, string origin)
    {
        var count = shelf.Books.Count;
        var title = count == 0 ? "A bookshelf on Shelfy" : $"A bookshelf with {count} book{(count == 1 ? "" : "s")} on Shelfy";
        var description = count == 0
            ? "An empty shelf, for now."
            : "Including " + string.Join(", ", shelf.Books.Take(3).Select(b => b.Title)) + (count > 3 ? " and more." : ".");
        var cover = shelf.Books.Select(b => b.CoverUrl).FirstOrDefault(IsHttpUrl);
        var image = cover is null ? "" : $"<meta property=\"og:image\" content=\"{Enc(cover)}\">";

        return template
            .Replace("{{TITLE}}", Enc(title))
            .Replace("{{DESCRIPTION}}", Enc(description))
            .Replace("{{URL}}", Enc($"{origin}/s/{shelf.Id}"))
            .Replace("{{IMAGE_TAG}}", image);
    }

    private static bool IsHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    private static string Enc(string s) => WebUtility.HtmlEncode(s);

    private static async Task<string?> ReadTemplateAsync(IWebHostEnvironment env, string name, CancellationToken ct)
    {
        var file = env.WebRootFileProvider.GetFileInfo(name);
        if (!file.Exists)
        {
            return null;
        }

        await using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }
}
