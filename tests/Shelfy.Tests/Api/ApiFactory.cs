using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shelfy.Domain;
using Shelfy.Features.Common.Email;
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
    public FakeMailer Mailer { get; } = new();
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
            services.RemoveAll<IMailer>();
            services.AddSingleton<IMailer>(Mailer);
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

/// <summary>Keeps every email instead of sending it, so tests can follow the confirmation link.</summary>
public sealed class FakeMailer : IMailer
{
    private readonly List<MailMessage> _sent = new();

    public IReadOnlyList<MailMessage> Sent
    {
        get { lock (_sent) { return _sent.ToList(); } }
    }

    public bool Fail { get; set; }

    public Task SendAsync(MailMessage message, CancellationToken ct = default)
    {
        if (Fail)
        {
            throw new InvalidOperationException("SMTP is down.");
        }

        lock (_sent) { _sent.Add(message); }
        return Task.CompletedTask;
    }

    /// <summary>The confirmation link (path and query) in the last email sent to this address.</summary>
    public string ConfirmLink(string to)
    {
        var body = Sent.Last(m => m.To == to).Body;
        var start = body.IndexOf("/api/account/confirm-email?", StringComparison.Ordinal);
        Assert.True(start >= 0, "No confirmation link in the email.");
        var end = body.IndexOfAny(new[] { '\n', ' ' }, start);
        return end < 0 ? body[start..] : body[start..end];
    }
}
