using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Shelfy.Features.Common;
using Shelfy.Features.Common.Email;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Users.LibraryCard;

/// <summary>
/// GET /api/account/card.svg: the signed-in member's library card (<see cref="LibraryCardSvg"/>). Generated per request from the
/// user id, the display name and the account's creation date; nothing is stored. The QR code points at the member's public
/// shelf when they have one (Shelf.IsPublic), otherwise at the site, so a card photo never leaks a private shelf link.
/// </summary>
public static class LibraryCardEndpoint
{
    public static IEndpointRouteBuilder MapLibraryCard(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/account/card.svg", async (ClaimsPrincipal user, HttpRequest http, IConfiguration config, IOptions<EmailOptions> email,
                    ShelfyDbContext db, CancellationToken ct) =>
                {
                    var id = user.UserId();
                    var account = id is null ? null : await db.Accounts.AsNoTracking().Where(a => a.Id == id).Select(a => new { a.Id, a.CreatedAt }).FirstOrDefaultAsync(ct);
                    var name = account is null ? null : await db.Users.AsNoTracking().Where(u => u.Id == account.Id).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
                    if (account is null || name is null)
                    {
                        return Results.Json(new { error = "Sign in first." }, statusCode: StatusCodes.Status401Unauthorized);
                    }

                    var publicShelf = await db.Shelves.AsNoTracking()
                        .Where(s => s.UserId == account.Id && s.IsPublic && !s.IsHidden)
                        .OrderBy(s => s.Id)
                        .Select(s => (Guid?)s.Id)
                        .FirstOrDefaultAsync(ct);
                    var origin = Origin(http, config, email.Value);
                    var qrUrl = publicShelf is { } shelfId ? $"{origin}/s/{shelfId}" : origin + "/";
                    var svg = LibraryCardSvg.Render(account.Id, name, account.CreatedAt, qrUrl);
                    http.HttpContext.Response.Headers.CacheControl = "private, no-cache";
                    return Results.Content(svg, "image/svg+xml; charset=utf-8");
                })
            .RequireAuthorization()
            .WithName("GetLibraryCard")
            .WithTags("Account")
            .Produces(StatusCodes.Status200OK, contentType: "image/svg+xml")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // The floating sample card that signed-out visitors see: a fixed demo member, so it is the same for everyone and cacheable.
        app.MapGet("/api/card/sample.svg", (HttpRequest http, IConfiguration config, IOptions<EmailOptions> email, TimeProvider clock) =>
                {
                    var svg = LibraryCardSvg.Render(SampleId, SampleName, clock.GetUtcNow().UtcDateTime, Origin(http, config, email.Value) + "/signin.html#register");
                    http.HttpContext.Response.Headers.CacheControl = "public, max-age=3600";
                    return Results.Content(svg, "image/svg+xml; charset=utf-8");
                })
            .WithName("GetSampleLibraryCard")
            .WithTags("Account")
            .Produces(StatusCodes.Status200OK, contentType: "image/svg+xml");

        return app;
    }

    /// <summary>The demo member behind the sample card (never a real account: ids are random v4 GUIDs).</summary>
    public static readonly Guid SampleId = new("5e1f0000-0000-4000-8000-00000000ca7d");

    public const string SampleName = "Your name here";

    private static string Origin(HttpRequest http, IConfiguration config, EmailOptions email)
    {
        var configured = config["Lending:PublicBaseUrl"] is { Length: > 0 } lending ? lending : email.PublicBaseUrl;
        return (string.IsNullOrWhiteSpace(configured) ? $"{http.Scheme}://{http.Host}" : configured.Trim()).TrimEnd('/');
    }
}
