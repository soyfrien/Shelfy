using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Features.Common.Email;
using Shelfy.Features.Users.Account;
using Shelfy.Features.Users.CreateGuest;

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
        if (string.Equals(configuration["Email:Provider"], "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IMailer, SmtpMailer>();
        }
        else
        {
            services.AddSingleton<IMailer, LogMailer>();
        }

        return services;
    }

    public static IEndpointRouteBuilder MapUsersModule(this IEndpointRouteBuilder app) => app.MapCreateGuest().MapAccount();
}
