using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Features.Books.SearchBooks;

namespace Shelfy.Features.Books;

public static class BooksModule
{
    public static IServiceCollection AddBooksModule(this IServiceCollection services)
    {
        services.AddScoped<SearchBooksHandler>();
        return services;
    }

    // Planned slice: Books/AddBook (docs/architecture.md).
    public static IEndpointRouteBuilder MapBooksModule(this IEndpointRouteBuilder app) =>
        app.MapSearchBooks();
}
