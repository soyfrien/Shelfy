using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Shelfy.Features.Recognition;

/// <summary>
/// Recognition module placeholder. Upload, the Service Bus consumer, Azure AI Vision OCR and
/// Google Books / OpenLibrary resolution (ADR-007) are added as slices once their feature specs exist.
/// </summary>
public static class RecognitionModule
{
    public static IServiceCollection AddRecognitionModule(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapRecognitionModule(this IEndpointRouteBuilder app) => app;
}
