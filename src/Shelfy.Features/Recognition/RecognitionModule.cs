using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shelfy.Features.Recognition.GetRecognitionJob;
using Shelfy.Features.Recognition.Ocr;
using Shelfy.Features.Recognition.Resolution;
using Shelfy.Features.Recognition.Storage;
using Shelfy.Features.Recognition.UploadShelfPhoto;
using Shelfy.Features.Recognition.Worker;

namespace Shelfy.Features.Recognition;

/// <summary>
/// Recognition module (ADR-007, ADR-008): photo upload, job status, and the background pipeline
/// photo -> OCR -> spine parsing -> book resolution -> shelf. See docs/pipeline.md.
/// </summary>
public static class RecognitionModule
{
    public const string AzureVisionClientName = "AzureVision";

    public static IServiceCollection AddRecognitionModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RecognitionOptions>(configuration.GetSection(RecognitionOptions.SectionName));
        services.Configure<PhotoStorageOptions>(configuration.GetSection(PhotoStorageOptions.SectionName));
        services.Configure<AzureVisionOptions>(configuration.GetSection(AzureVisionOptions.SectionName));
        services.Configure<ResolutionOptions>(configuration.GetSection(ResolutionOptions.SectionName));
        services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = UploadShelfPhotoEndpoint.RequestLimitBytes);

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<UploadShelfPhotoHandler>();
        services.AddScoped<GetRecognitionJobHandler>();

        // Photo storage: local filesystem by default; AzureBlob is a stub.
        services.AddSingleton<IPhotoStorage>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<PhotoStorageOptions>>().Value;
            return o.Provider.ToLowerInvariant() switch
            {
                "local" => new LocalFileSystemPhotoStorage(o.LocalPath),
                "azureblob" => new AzureBlobPhotoStorage(),
                _ => throw new InvalidOperationException($"Unknown Storage:Provider '{o.Provider}'. Use Local or AzureBlob.")
            };
        });

        AddOcr(services);
        AddResolution(services);

        services.AddSingleton<IJobQueue, DbJobQueue>();
        services.AddScoped<RecognitionJobProcessor>();

        var recognition = configuration.GetSection(RecognitionOptions.SectionName).Get<RecognitionOptions>() ?? new RecognitionOptions();
        if (recognition.WorkerEnabled)
        {
            services.AddSingleton<RecognitionWorker>();
            services.AddHostedService(sp => sp.GetRequiredService<RecognitionWorker>());
        }

        return services;
    }

    public static IEndpointRouteBuilder MapRecognitionModule(this IEndpointRouteBuilder app) => app
        .MapUploadShelfPhoto()
        .MapGetRecognitionJob();

    private static void AddOcr(IServiceCollection services)
    {
        services.AddHttpClient(AzureVisionClientName).ConfigureHttpClient((sp, client) =>
        {
            var o = sp.GetRequiredService<IOptions<AzureVisionOptions>>().Value;
            if (!o.IsConfigured)
            {
                return;
            }

            client.BaseAddress = new Uri(o.Endpoint!.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add(AzureVisionTextRecognizer.KeyHeader, o.Key);
            client.Timeout = TimeSpan.FromSeconds(Math.Max(5, o.TimeoutSeconds));
            client.MaxResponseContentBufferSize = 8 * 1024 * 1024;
        });

        services.AddScoped<ITextRecognizer>(sp =>
        {
            var recognition = sp.GetRequiredService<IOptions<RecognitionOptions>>().Value;
            var vision = sp.GetRequiredService<IOptions<AzureVisionOptions>>().Value;

            if (recognition.OcrProvider.Equals("Fake", StringComparison.OrdinalIgnoreCase))
            {
                sp.GetRequiredService<ILogger<FakeTextRecognizer>>()
                    .LogWarning("Recognition:OcrProvider=Fake - using canned OCR data. Do not use in production.");
                return new FakeTextRecognizer();
            }

            return vision.IsConfigured
                ? new AzureVisionTextRecognizer(sp.GetRequiredService<IHttpClientFactory>().CreateClient(AzureVisionClientName), vision.ApiVersion)
                : new NotConfiguredTextRecognizer();
        });
    }

    private static void AddResolution(IServiceCollection services)
    {
        static void Configure(IServiceProvider sp, HttpClient client, string baseAddress)
        {
            var o = sp.GetRequiredService<IOptions<ResolutionOptions>>().Value;
            client.BaseAddress = new Uri(baseAddress);
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, o.TimeoutSeconds));
            client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(o.UserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        static DelegatingHandler Retry(IServiceProvider sp)
        {
            var o = sp.GetRequiredService<IOptions<ResolutionOptions>>().Value;
            return new RetryHandler(o.MaxRetries, TimeSpan.FromMilliseconds(o.RetryBaseDelayMilliseconds));
        }

        services.AddHttpClient<OpenLibraryBookResolver>((sp, c) => Configure(sp, c, "https://openlibrary.org/"))
            .AddHttpMessageHandler(Retry);

        services.AddHttpClient<GoogleBooksResolver>((sp, c) => Configure(sp, c, "https://www.googleapis.com/"))
            .AddTypedClient((http, sp) =>
                new GoogleBooksResolver(http, sp.GetRequiredService<IOptions<ResolutionOptions>>().Value.GoogleBooksApiKey))
            .AddHttpMessageHandler(Retry);

        // OpenLibrary first, Google Books as fallback.
        services.AddScoped<IBookResolver>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<ResolutionOptions>>().Value;
            var resolvers = new List<IBookResolver>();
            if (o.OpenLibraryEnabled)
            {
                resolvers.Add(sp.GetRequiredService<OpenLibraryBookResolver>());
            }

            if (o.GoogleBooksEnabled)
            {
                resolvers.Add(sp.GetRequiredService<GoogleBooksResolver>());
            }

            return new CompositeBookResolver(resolvers, sp.GetRequiredService<IOptions<RecognitionOptions>>(),
                sp.GetRequiredService<ILogger<CompositeBookResolver>>());
        });
    }
}
