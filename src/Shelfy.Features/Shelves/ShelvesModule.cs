using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Features.Shelves.CreateShelf;
using Shelfy.Features.Shelves.GetShelf;

namespace Shelfy.Features.Shelves;

public static class ShelvesModule
{
    public static IServiceCollection AddShelvesModule(this IServiceCollection services)
    {
        services.AddScoped<CreateShelfHandler>();
        services.AddScoped<GetShelfHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapShelvesModule(this IEndpointRouteBuilder app) =>
        app.MapCreateShelf().MapGetShelf();
}
