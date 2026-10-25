using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Shelfy.Features.Users;

/// <summary>
/// Users module placeholder. Slices (e.g. GetCurrentUser, linking the Entra External ID subject)
/// are added here after the identity setup is specified (ADR-006).
/// </summary>
public static class UsersModule
{
    public static IServiceCollection AddUsersModule(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapUsersModule(this IEndpointRouteBuilder app) => app;
}
