using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Features.Common.Email;
using Shelfy.Features.Users.Account;
using Shelfy.Features.Users.CreateGuest;
using Shelfy.Features.Users.LibraryCard;

namespace Shelfy.Features.Users;

/// <summary>
/// Users module: anonymous guests (the original web UI flow) and accounts (email + password, ADR-012).
/// </summary>
public static class UsersModule
{
    public static IServiceCollection AddUsersModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddShelfyAuth(configuration);
        services.AddScoped<CreateGuestHandler>();
        services.AddScoped<AccountHandler>();
        services.AddMemoryCache();
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Section));
        switch (configuration["Email:Provider"]?.Trim().ToLowerInvariant())
        {
            case "acs":
                services.AddSingleton<IMailer, AcsMailer>();
                break;
            case "smtp":
                services.AddSingleton<IMailer, SmtpMailer>();
                break;
            default:
                services.AddSingleton<IMailer, LogMailer>();
                break;
        }

        return services;
    }

    public static IEndpointRouteBuilder MapUsersModule(this IEndpointRouteBuilder app) => app.MapCreateGuest().MapAccount().MapLibraryCard();
}
