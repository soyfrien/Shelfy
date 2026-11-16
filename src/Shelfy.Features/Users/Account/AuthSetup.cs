using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Users.Account;

/// <summary>
/// ASP.NET Core Identity with a cookie for both the web pages and the API (ADR-012). The cookie is HttpOnly, SameSite=Lax and
/// Secure over HTTPS; API calls get 401/403 instead of a redirect. Data-protection keys (which sign the cookie) are kept in
/// <c>DataProtection:KeysPath</c> when set, so sign-ins survive restarts and redeploys.
/// </summary>
public static class AuthSetup
{
    public const string CookieName = "shelfy.auth";

    public static IServiceCollection AddShelfyAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var dp = services.AddDataProtection().SetApplicationName("Shelfy");
        var keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            dp.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.AddIdentityCore<ShelfyAccount>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 8;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireDigit = false;
                o.Lockout.MaxFailedAccessAttempts = 8;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
            })
            .AddEntityFrameworkStores<ShelfyDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = CookieName;
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromDays(30);
            o.SlidingExpiration = true;
            o.LoginPath = "/signin.html";
            o.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = ctx => Deny(ctx, StatusCodes.Status401Unauthorized, "Sign in first."),
                OnRedirectToAccessDenied = ctx => Deny(ctx, StatusCodes.Status403Forbidden, "Not allowed.")
            };
        });
        services.AddAuthorization();
        return services;
    }

    private static Task Deny(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> ctx, int status, string error)
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = status;
            return ctx.Response.WriteAsJsonAsync(new { error });
        }

        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }
}
