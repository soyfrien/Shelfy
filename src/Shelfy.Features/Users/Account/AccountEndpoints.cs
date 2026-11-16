using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shelfy.Features.Common;

namespace Shelfy.Features.Users.Account;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccount(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account").WithTags("Account");

        group.MapPost("/register", async (RegisterRequest request, AccountHandler handler, CancellationToken ct) =>
                (await handler.RegisterAsync(request, ct)).ToHttpResult(me => Results.Created("/api/account/me", me)))
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

        return app;
    }
}
