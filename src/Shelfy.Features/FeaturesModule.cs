using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Features.Books;
using Shelfy.Features.Recognition;
using Shelfy.Features.Shelves;
using Shelfy.Features.Users;

namespace Shelfy.Features;

/// <summary>Single entry point that registers every v1 module. Trades, Messaging and Recommendations are not in v1.</summary>
public static class FeaturesModule
{
    public static IServiceCollection AddShelfyFeatures(this IServiceCollection services, IConfiguration configuration) => services
        .AddUsersModule()
        .AddShelvesModule()
        .AddBooksModule()
        .AddRecognitionModule(configuration);

    public static IEndpointRouteBuilder MapShelfyFeatures(this IEndpointRouteBuilder app) => app
        .MapUsersModule()
        .MapShelvesModule()
        .MapBooksModule()
        .MapRecognitionModule();
}
