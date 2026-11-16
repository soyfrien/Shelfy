using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Features.Shelves.CreateShelf;
using Shelfy.Features.Shelves.DeleteShelf;
using Shelfy.Features.Shelves.GetShelf;
using Shelfy.Features.Shelves.HideShelf;
using Shelfy.Features.Shelves.SetShelfPublic;
using Shelfy.Features.Shelves.ShelfPage;

namespace Shelfy.Features.Shelves;

public static class ShelvesModule
{
    public static IServiceCollection AddShelvesModule(this IServiceCollection services)
    {
        services.AddScoped<CreateShelfHandler>();
        services.AddScoped<GetShelfHandler>();
        services.AddScoped<HideShelfHandler>();
        services.AddScoped<SetShelfPublicHandler>();
        services.AddScoped<DeleteShelfHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapShelvesModule(this IEndpointRouteBuilder app) =>
        app.MapCreateShelf().MapGetShelf().MapHideShelf().MapSetShelfPublic().MapDeleteShelf().MapShelfPage();
}
