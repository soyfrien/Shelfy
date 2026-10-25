using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Features.Users.CreateGuest;

namespace Shelfy.Features.Users;

/// <summary>
/// Users module. v1 has only anonymous guests (for the web UI); real slices (e.g. GetCurrentUser, linking the
/// Entra External ID subject) are added here after the identity setup is specified (ADR-006).
/// </summary>
public static class UsersModule
{
    public static IServiceCollection AddUsersModule(this IServiceCollection services)
    {
        services.AddScoped<CreateGuestHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapUsersModule(this IEndpointRouteBuilder app) => app.MapCreateGuest();
}
