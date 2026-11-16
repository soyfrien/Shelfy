using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Shelfy.Features.Common;
using Shelfy.Features.Common.Email;

namespace Shelfy.Features.Users.Account;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccount(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account").WithTags("Account");

        group.MapPost("/register", async (RegisterRequest request, HttpRequest http, IOptions<EmailOptions> email, AccountHandler handler, CancellationToken ct) =>
                (await handler.RegisterAsync(request, BaseUrl(http, email.Value), ct)).ToHttpResult(me => Results.Created("/api/account/me", me)))
            .WithName("Register")
            .Produces<MeResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/login", async (LoginRequest request, AccountHandler handler, CancellationToken ct) =>
                (await handler.LoginAsync(request, ct)).ToHttpResult())
            .WithName("Login")
            .Produces<MeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (AccountHandler handler) =>
            {
                await handler.LogoutAsync();
                return Results.NoContent();
            })
            .WithName("Logout")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", async (ClaimsPrincipal user, AccountHandler handler, CancellationToken ct) =>
                (await handler.GetMeAsync(user.UserId(), ct)).ToHttpResult())
            .WithName("GetMe")
            .Produces<MeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPut("/profile", async (UpdateProfileRequest request, ClaimsPrincipal user, AccountHandler handler, CancellationToken ct) =>
                (await handler.UpdateProfileAsync(user.UserId(), request, ct)).ToHttpResult())
            .RequireAuthorization()
            .WithName("UpdateProfile")
            .Produces<MeResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // The link in the confirmation email. Redirects to the sign-in page, which says whether it worked.
        group.MapGet("/confirm-email", async (Guid? userId, string? code, AccountHandler handler) =>
                Results.Redirect(await handler.ConfirmEmailAsync(userId, code) ? "/signin.html?confirmed=1" : "/signin.html?confirmed=0"))
            .WithName("ConfirmEmail")
            .Produces(StatusCodes.Status302Found);

        group.MapPost("/confirm-email/resend", async (ClaimsPrincipal user, HttpRequest http, IOptions<EmailOptions> email, AccountHandler handler, CancellationToken ct) =>
                (await handler.ResendConfirmationAsync(user.UserId(), BaseUrl(http, email.Value), ct)).ToHttpResult())
            .RequireAuthorization()
            .WithName("ResendConfirmationEmail")
            .Produces<MeResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary><c>Email:PublicBaseUrl</c> when set (production, behind nginx); otherwise the address this request came in on.</summary>
    private static string BaseUrl(HttpRequest http, EmailOptions email) =>
        string.IsNullOrWhiteSpace(email.PublicBaseUrl) ? $"{http.Scheme}://{http.Host}" : email.PublicBaseUrl.Trim();
}
