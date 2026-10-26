using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shelfy.Domain;
using Shelfy.Features.Recognition;
using Shelfy.Features.Recognition.Resolution;
using Shelfy.Features.Recognition.Worker;
using Shelfy.Infrastructure;
using Shelfy.Tests.Support;

namespace Shelfy.Tests.Api;

/// <summary>The real API host over a throwaway SQLite database, a temp photo folder, fake OCR data and a fake resolver. No network.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteTestDatabase _database = new();
    private readonly string _photos = Path.Combine(Path.GetTempPath(), "shelfy-api-photos-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> _extraConfig;

    public ApiFactory() : this(null)
    {
    }

    internal ApiFactory(Dictionary<string, string?>? extraConfig)
    {
        _extraConfig = extraConfig ?? new();
        Resolver.Matches["The Hobbit"] = FakeBookResolver.Match("9780547928227", "The Hobbit", "J.R.R. Tolkien");
        Resolver.Matches["Dune"] = FakeBookResolver.Match("9780441172719", "Dune", "Frank Herbert");
    }

    public FakeBookResolver Resolver { get; } = new();
    public FakeBookSearch Search { get; } = new();
    public string PhotoFolder => _photos;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Recognition:WorkerEnabled", "false"); // tests drive the worker by hand
        builder.UseSetting("Recognition:OcrProvider", "Fake");
        builder.UseSetting("Storage:LocalPath", _photos);
        foreach (var (key, value) in _extraConfig)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ShelfyDbContext>>();
            services.AddDbContext<ShelfyDbContext>(o => o.UseSqlite(_database.ConnectionString));
            services.RemoveAll<IBookResolver>();
            services.AddSingleton<IBookResolver>(Resolver);
            services.RemoveAll<IBookSearch>();
            services.AddSingleton<IBookSearch>(Search);
        });
    }

    public async Task<Guid> CreateUserAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Louis", City = "Brooklyn" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Processes one queued job exactly as the hosted worker would.</summary>
    public Task<bool> RunWorkerOnceAsync()
    {
        var worker = new RecognitionWorker(
            Services.GetRequiredService<IServiceScopeFactory>(),
            Services.GetRequiredService<IJobQueue>(),
            Services.GetRequiredService<IOptions<RecognitionOptions>>(),
            NullLogger<RecognitionWorker>.Instance);
        return worker.RunOnceAsync(CancellationToken.None);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _database.Dispose();
            if (Directory.Exists(_photos))
            {
                Directory.Delete(_photos, recursive: true);
            }
        }
    }
}
