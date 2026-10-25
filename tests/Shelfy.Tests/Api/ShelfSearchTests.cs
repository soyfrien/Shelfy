using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Domain;
using Shelfy.Infrastructure;

namespace Shelfy.Tests.Api;

/// <summary>The shelf search UI: its static pieces, and GET /api/shelves/{id}/unmatched, which feeds the "couldn't match" part of the search.</summary>
public class ShelfSearchTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public ShelfSearchTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<Guid> NewShelfAsync()
    {
        var userId = await _factory.CreateUserAsync();
        var response = await _client.PostAsJsonAsync("/api/shelves", new { userId });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>One completed job with the given spines: text, and the book it resolved to (null = not matched).</summary>
    private async Task AddJobAsync(Guid shelfId, DateTime at, RecognitionJobStatus status, params (string Raw, bool Matched)[] spines)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
        var job = RecognitionJob.CreatePending(shelfId, "k" + Guid.NewGuid(), "image/jpeg", 1, Guid.NewGuid().ToString("N"), 4, at);
        job.Status = status;
        db.RecognitionJobs.Add(job);
        var position = 0;
        foreach (var (raw, matched) in spines)
        {
            var spine = new DetectedSpine { Id = Guid.NewGuid(), RecognitionJobId = job.Id, Position = position++, RawText = raw, CandidateTitle = raw.ToUpperInvariant() };
            if (matched)
            {
                var book = new Book { Id = Guid.NewGuid(), Title = raw, Author = "A", Isbn = Guid.NewGuid().ToString("N")[..13] };
                db.Books.Add(book);
                spine.MarkResolved(book, 0.9, "Test");
            }

            db.DetectedSpines.Add(spine);
        }

        await db.SaveChangesAsync();
    }

    private async Task<string[]> UnmatchedAsync(Guid shelfId)
    {
        var response = await _client.GetAsync($"/api/shelves/{shelfId}/unmatched");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("spines").EnumerateArray().Select(s => s.GetProperty("text").GetString()!).ToArray();
    }

    [Fact]
    public async Task Unmatched_Lists_The_Unresolved_Spines_Of_All_Photos_Once_Each()
    {
        var shelf = await NewShelfAsync();
        var other = await NewShelfAsync();
        var t = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        await AddJobAsync(shelf, t, RecognitionJobStatus.Completed, ("Dune", true), ("TOLKIEN HOBBT", false), ("zzz", false), ("  ", false));
        await AddJobAsync(shelf, t.AddMinutes(1), RecognitionJobStatus.Completed, ("tolkien hobbt", false), ("Asimov Foundation", false));
        await AddJobAsync(shelf, t.AddMinutes(2), RecognitionJobStatus.Failed, ("from a failed job", false));
        await AddJobAsync(other, t, RecognitionJobStatus.Completed, ("someone else's spine", false));

        Assert.Equal(new[] { "TOLKIEN HOBBT", "zzz", "Asimov Foundation" }, await UnmatchedAsync(shelf));
        Assert.Equal(new[] { "someone else's spine" }, await UnmatchedAsync(other));
    }

    [Fact]
    public async Task Unmatched_Is_Empty_For_A_Shelf_Without_Photos_And_404_For_Unknown_Or_Hidden_Shelves()
    {
        var shelf = await NewShelfAsync();
        Assert.Empty(await UnmatchedAsync(shelf));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/shelves/{Guid.NewGuid()}/unmatched")).StatusCode);

        await _client.PostAsync($"/api/shelves/{shelf}/hide", null);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/shelves/{shelf}/unmatched")).StatusCode);
    }

    [Fact]
    public async Task Both_Pages_Load_The_Search_Script_Before_The_Shared_Script_And_The_Home_Page_Has_A_Titled_Unmatched_List()
    {
        var home = await _client.GetStringAsync("/");
        Assert.True(home.IndexOf("/search.js", StringComparison.Ordinal) is > 0 && home.IndexOf("/search.js", StringComparison.Ordinal) < home.IndexOf("/shared.js", StringComparison.Ordinal));
        Assert.Contains("id=\"unresolvedTitle\"", home);

        var shelf = await NewShelfAsync();
        var page = await _client.GetStringAsync($"/s/{shelf}");
        Assert.True(page.IndexOf("/search.js", StringComparison.Ordinal) is > 0 && page.IndexOf("/search.js", StringComparison.Ordinal) < page.IndexOf("/shared.js", StringComparison.Ordinal));

        var css = await _client.GetStringAsync("/style.css");
        Assert.Contains(".searchbar", css);
        Assert.Contains("position: sticky", css);
        Assert.Contains("font-size: 16px", css);   // no iOS zoom on focus

        var js = await _client.GetStringAsync("/shared.js");
        Assert.Contains("Search title or author", js);
    }
}
