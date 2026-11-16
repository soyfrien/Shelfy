using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shelfy.Domain;
using Shelfy.Features.Recognition;
using Shelfy.Features.Recognition.Ocr;
using Shelfy.Features.Recognition.Resolution;
using Shelfy.Features.Recognition.Storage;
using Shelfy.Features.Recognition.Worker;
using Shelfy.Infrastructure;

namespace Shelfy.Tests.Support;

/// <summary>Manually advanced clock.</summary>
public class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public TestClock() : this(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero)) { }
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

public static class Fixtures
{
    public static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath));
}

/// <summary>Counts requests and answers from a delegate. Never touches the network.</summary>
public class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> Bodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? "" : $"{(await request.Content.ReadAsByteArrayAsync(cancellationToken)).Length} bytes");
        return respond(request);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static StubHandler Always(string json, HttpStatusCode code = HttpStatusCode.OK) => new(_ => Json(json, code));
    public static StubHandler Sequence(params Func<HttpResponseMessage>[] responses)
    {
        var i = 0;
        return new StubHandler(_ => responses[Math.Min(i++, responses.Length - 1)]());
    }
}

public class InMemoryPhotoStorage : IPhotoStorage
{
    public Dictionary<string, byte[]> Files { get; } = new();

    public async Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        Files[key] = ms.ToArray();
        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
        Files.TryGetValue(key, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes))
            : throw new FileNotFoundException(key);

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        Files.Remove(key);
        return Task.CompletedTask;
    }

    public Task DeleteFolderAsync(string prefix, CancellationToken ct = default)
    {
        foreach (var key in Files.Keys.Where(k => k.StartsWith(prefix.TrimEnd('/') + "/", StringComparison.Ordinal)).ToList())
        {
            Files.Remove(key);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Answers lookups from a dictionary keyed by candidate title (case-insensitive).</summary>
public class FakeBookResolver : IBookResolver
{
    public Dictionary<string, BookMatch> Matches { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<BookQuery> Queries { get; } = new();
    public Exception? ThrowOnResolve { get; set; }
    public string Name => "Fake";

    public Task<BookMatch?> ResolveAsync(BookQuery query, CancellationToken ct = default)
    {
        Queries.Add(query);
        if (ThrowOnResolve is not null)
        {
            throw ThrowOnResolve;
        }

        return Task.FromResult(Matches.GetValueOrDefault(query.Title));
    }

    public static BookMatch Match(string isbn, string title, string author, double score = 0.95) =>
        new(isbn, title, author, null, score, "Fake");
}

/// <summary>Catalog search for the tap-to-fix sheet: answers from lists, records what was asked.</summary>
public class FakeBookSearch : IBookSearch
{
    public string Name => "FakeSearch";

    /// <summary>Answers for structured searches, keyed by query title (case-insensitive).</summary>
    public Dictionary<string, List<BookMatch>> ByTitle { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Answers for free-text searches, keyed by the exact text (case-insensitive).</summary>
    public Dictionary<string, List<BookMatch>> ByText { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Asked { get; } = new();
    public Exception? Throw { get; set; }

    public Task<IReadOnlyList<BookMatch>> SearchAsync(BookQuery query, int limit, CancellationToken ct = default)
    {
        Asked.Add($"structured:{query.Title}|{query.Author}");
        if (Throw is not null) throw Throw;
        return Task.FromResult<IReadOnlyList<BookMatch>>(ByTitle.GetValueOrDefault(query.Title) ?? new List<BookMatch>());
    }

    public Task<IReadOnlyList<BookMatch>> SearchTextAsync(string text, int limit, CancellationToken ct = default)
    {
        Asked.Add("text:" + text);
        if (Throw is not null) throw Throw;
        return Task.FromResult<IReadOnlyList<BookMatch>>(ByText.GetValueOrDefault(text) ?? new List<BookMatch>());
    }
}

/// <summary>Returns one scripted OCR result per call, in order (the tiles of one batch).</summary>
public class ScriptedRecognizer(params OcrResult[] results) : ITextRecognizer
{
    private int _next;
    public Task<OcrResult> RecognizeAsync(Stream image, string contentType, CancellationToken ct = default) =>
        Task.FromResult(results[Math.Min(_next++, results.Length - 1)]);

    public static OcrResult Lines(params string[] lines) => new(null, null, lines.Select(OcrLine.TextOnly).ToList());
}

public class ThrowingRecognizer(Exception ex) : ITextRecognizer
{
    public Task<OcrResult> RecognizeAsync(Stream image, string contentType, CancellationToken ct = default) => throw ex;
}

/// <summary>
/// A throwaway SQLite database file (a real relational provider, so ExecuteUpdate and conditional claiming run for real).
/// Each context opens its own connection, so concurrent use behaves like separate workers. PostgreSQL itself is not exercised.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"shelfy-test-{Guid.NewGuid():N}.db");

    public SqliteTestDatabase()
    {
        ConnectionString = new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString();
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public string ConnectionString { get; }

    public ShelfyDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ShelfyDbContext>().UseSqlite(ConnectionString).Options);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm", _path + "-journal" })
        {
            if (File.Exists(f))
            {
                File.Delete(f);
            }
        }
    }
}

/// <summary>Everything the worker needs, wired by hand over SQLite, in-memory storage and fakes.</summary>
public sealed class PipelineHarness : IDisposable
{
    public SqliteTestDatabase Database { get; } = new();
    public TestClock Clock { get; } = new();
    public InMemoryPhotoStorage Storage { get; } = new();
    public FakeBookResolver Resolver { get; } = new();
    public ITextRecognizer Recognizer { get; set; } = new FakeTextRecognizer();
    public RecognitionOptions Options { get; } = new() { MaxAttempts = 3, BackoffBaseSeconds = 30, BackoffMaxSeconds = 600, MinMatchScore = 0.7 };
    public ServiceProvider Services { get; private set; } = null!;
    public IJobQueue Queue => Services.GetRequiredService<IJobQueue>();
    public RecognitionWorker Worker => Services.GetRequiredService<RecognitionWorker>();

    public PipelineHarness Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IOptions<RecognitionOptions>>(Options2());
        services.AddDbContext<ShelfyDbContext>(o => o.UseSqlite(Database.ConnectionString));
        services.AddSingleton<IPhotoStorage>(Storage);
        services.AddSingleton<IBookResolver>(Resolver);
        services.AddScoped<ITextRecognizer>(_ => Recognizer);
        services.AddSingleton<IJobQueue, DbJobQueue>();
        services.AddScoped<RecognitionJobProcessor>();
        services.AddSingleton<RecognitionWorker>();
        Services = services.BuildServiceProvider();
        return this;
    }

    private IOptions<RecognitionOptions> Options2() => Microsoft.Extensions.Options.Options.Create(Options);

    /// <summary>Creates a user, a shelf and a pending job for a stored fake photo.</summary>
    public async Task<(Guid ShelfId, Guid JobId)> SeedJobAsync(Guid? shelfId = null, Guid? batchId = null, int part = 0, int parts = 0)
    {
        await using var db = Database.CreateContext();
        Guid sid;
        if (shelfId is null)
        {
            var user = new User { Id = Guid.NewGuid(), DisplayName = "Louis", City = "Brooklyn" };
            var shelf = new Shelf { Id = Guid.NewGuid(), UserId = user.Id };
            db.Users.Add(user);
            db.Shelves.Add(shelf);
            sid = shelf.Id;
        }
        else
        {
            sid = shelfId.Value;
        }

        var key = $"shelves/{sid:N}/{Guid.NewGuid():N}.jpg";
        Storage.Files[key] = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
        var job = RecognitionJob.CreatePending(sid, key, "image/jpeg", 7, Guid.NewGuid().ToString("N"), Options.MaxAttempts, Clock.GetUtcNow().UtcDateTime,
            batchId, part, parts);
        if (batchId is not null)
        {
            job.NextAttemptAt = job.NextAttemptAt.AddMilliseconds(part - 1000); // claim order = part order, all due now
        }

        db.RecognitionJobs.Add(job);
        await db.SaveChangesAsync();
        return (sid, job.Id);
    }

    public async Task<RecognitionJob> GetJobAsync(Guid id)
    {
        await using var db = Database.CreateContext();
        return await db.RecognitionJobs.AsNoTracking().SingleAsync(j => j.Id == id);
    }

    public void Dispose()
    {
        Services?.Dispose();
        Database.Dispose();
    }
}

public static class TestImages
{
    public static byte[] Jpeg(int extraBytes = 16) => Pad(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 }, extraBytes);
    public static byte[] Png(int extraBytes = 16) => Pad(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D }, extraBytes);

    /// <summary>A PNG signature and IHDR with the given size (enough for the size check; not a decodable image).</summary>
    public static byte[] PngOfSize(int width, int height, int extraBytes = 64)
    {
        var bytes = Pad(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
            (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
            (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height, 8, 2, 0, 0, 0 }, extraBytes);
        return bytes;
    }

    /// <summary>A JPEG with JFIF and EXIF-like segments before a baseline frame header of the given size.</summary>
    public static byte[] JpegOfSize(int width, int height, int extraBytes = 64)
    {
        var app0 = new byte[] { 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1, 1, 0, 0, 1, 0, 1, 0, 0 };
        var app1 = new byte[] { 0xFF, 0xE1, 0x00, 0x08, 1, 2, 3, 4, 5, 6 };
        var sof = new byte[] { 0xFF, 0xC0, 0x00, 0x11, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1 };
        return Pad(new byte[] { 0xFF, 0xD8 }.Concat(app0).Concat(app1).Concat(sof).ToArray(), extraBytes);
    }

    private static byte[] Pad(byte[] header, int extra)
    {
        var bytes = new byte[header.Length + extra];
        header.CopyTo(bytes, 0);
        new Random(42).NextBytes(bytes.AsSpan(header.Length));
        return bytes;
    }
}
