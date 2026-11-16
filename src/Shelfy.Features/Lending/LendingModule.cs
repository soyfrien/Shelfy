using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shelfy.Features.Lending.Copies;
using Shelfy.Features.Lending.CopyPage;
using Shelfy.Features.Lending.Loans;

namespace Shelfy.Features.Lending;

/// <summary>Physical copies, borrowing, reservations and the copy label page (ADR-012).</summary>
public static class LendingModule
{
    public static IServiceCollection AddLendingModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ICopyCodeGenerator, RandomCopyCodeGenerator>();
        services.AddScoped<CopiesHandler>();
        services.AddScoped<LoansHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapLendingModule(this IEndpointRouteBuilder app) => app.MapCopies().MapLoans().MapCopyPage();
}
