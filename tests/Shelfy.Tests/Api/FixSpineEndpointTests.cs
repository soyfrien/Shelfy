using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Domain;
using Shelfy.Features.Recognition;
using Shelfy.Features.Recognition.Resolution;
using Shelfy.Infrastructure;
using Shelfy.Tests.Support;

namespace Shelfy.Tests.Api;

/// <summary>Tap to fix (ADR-011): suggestions, resolve, dismiss, the remembered correction and the noise filter, through the real API host.</summary>
public class FixSpineEndpointTests : IDisposable
{
    private const string FoundationIsbn = "9780553293357";
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public FixSpineEndpointTests() => _client = _factory.CreateClient();

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

    private async Task<List<Guid>> AddJobAsync(Guid shelfId, params (string Raw, string Title, string? Author, double? Conf)[] spines)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
        var job = RecognitionJob.CreatePending(shelfId, "k" + Guid.NewGuid(), "image/jpeg", 1, Guid.NewGuid().ToString("N"), 4, DateTime.UtcNow);
        job.Status = RecognitionJobStatus.Completed;
        db.RecognitionJobs.Add(job);
        var ids = new List<Guid>();
        var pos = 0;
        foreach (var (raw, title, author, conf) in spines)
        {
            var s = new DetectedSpine { Id = Guid.NewGuid(), RecognitionJobId = job.Id, Position = pos++, RawText = raw, CandidateTitle = title, CandidateAuthor = author, Confidence = conf };
            db.DetectedSpines.Add(s);
            ids.Add(s.Id);
        }

        await db.SaveChangesAsync();
        return ids;
    }

    private async Task<JsonElement> GetJsonAsync(string url, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await _client.GetAsync(url);
        Assert.Equal(expected, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string url, object? body = null)
    {
        var response = await _client.PostAsJsonAsync(url, body ?? new { });
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private async Task<string[]> UnmatchedAsync(Guid shelf, bool includeHidden = false)
    {
        var json = await GetJsonAsync($"/api/shelves/{shelf}/unmatched" + (includeHidden ? "?includeHidden=true" : ""));
        return json.GetProperty("spines").EnumerateArray().Select(s => s.GetProperty("text").GetString()!).ToArray();
    }

    private async Task<string[]> ShelfTitlesAsync(Guid shelf) =>
        (await GetJsonAsync($"/api/shelves/{shelf}")).GetProperty("books").EnumerateArray().Select(b => b.GetProperty("title").GetString()!).ToArray();

    private static object Foundation => new { isbn13 = FoundationIsbn, title = "Foundation", author = "Isaac Asimov", coverUrl = "https://covers.example/f.jpg" };

    // ---- the list ----

    [Fact]
    public async Task Unmatched_Gives_An_Id_Per_Spine_Hides_Noise_And_Counts_It()
    {
        var shelf = await NewShelfAsync();
        await AddJobAsync(shelf, ("Foundation | Isaac Asimov", "Foundation", "Isaac Asimov", 0.9), ("TH", "TH", null, 0.6), ("WITH", "WITH", null, 0.9), ("Hyperion Dan Simmons", "Hyperion", "Dan Simmons", 0.8));

        var json = await GetJsonAsync($"/api/shelves/{shelf}/unmatched");

        var spines = json.GetProperty("spines").EnumerateArray().ToList();
        Assert.Equal(new[] { "Foundation | Isaac Asimov", "Hyperion Dan Simmons" }, spines.Select(s => s.GetProperty("text").GetString()));
        Assert.All(spines, s => Assert.NotEqual(Guid.Empty, s.GetProperty("id").GetGuid()));
        Assert.Equal(2, json.GetProperty("hiddenLowQuality").GetInt32());

        var all = await GetJsonAsync($"/api/shelves/{shelf}/unmatched?includeHidden=true");
        Assert.Equal(4, all.GetProperty("spines").GetArrayLength());
        Assert.Equal(new[] { "short", "filler" }, all.GetProperty("spines").EnumerateArray().Where(s => s.GetProperty("lowQuality").GetBoolean()).Select(s => s.GetProperty("reason").GetString()));
    }

    // ---- suggestions ----

    [Fact]
    public async Task Suggestions_Try_The_Strategies_Show_Cover_Title_And_Author_And_Rank_By_Fit()
    {
        var shelf = await NewShelfAsync();
        var ids = await AddJobAsync(shelf, ("Foundaton | Isaac Asimov | NEW YORK TIMES", "Foundaton", "Isaac Asimov", 0.9));
        _factory.Search.ByText["Foundaton Isaac Asimov NEW YORK TIMES"] = new List<BookMatch>
        {
            new("9780000000001", "Foundation and Empire", "Isaac Asimov", null, 0, "OpenLibrary"),
            new("9780000000002", "Cooking With Fire", "Somebody Else", null, 0, "OpenLibrary")
        };
        _factory.Search.ByTitle["Foundaton"] = new List<BookMatch>
        {
            new(FoundationIsbn, "Foundation", "Isaac Asimov", "https://covers.example/f.jpg", 0.9, "OpenLibrary")
        };

        var json = await GetJsonAsync($"/api/shelves/{shelf}/spines/{ids[0]}/suggestions");

        Assert.Equal(ids[0], json.GetProperty("spineId").GetGuid());
        Assert.False(json.GetProperty("unavailable").GetBoolean());
        var s = json.GetProperty("suggestions").EnumerateArray().ToList();
        Assert.Equal(FoundationIsbn, s[0].GetProperty("isbn13").GetString());          // best fit first
        Assert.Equal("Foundation", s[0].GetProperty("title").GetString());
        Assert.Equal("Isaac Asimov", s[0].GetProperty("author").GetString());
        Assert.Equal("https://covers.example/f.jpg", s[0].GetProperty("coverUrl").GetString());
        Assert.Contains(s, x => x.GetProperty("title").GetString() == "Foundation and Empire");
        Assert.DoesNotContain(s, x => x.GetProperty("title").GetString() == "Cooking With Fire");   // too unlike the text
        Assert.True(s.Count <= 5);
        Assert.True(s[0].GetProperty("score").GetDouble() > s[1].GetProperty("score").GetDouble());
        Assert.Contains("text:Foundaton Isaac Asimov NEW YORK TIMES", _factory.Search.Asked);
        Assert.Contains("structured:Foundaton|Isaac Asimov", _factory.Search.Asked);
        Assert.Contains("text:Foundaton", _factory.Search.Asked);                         // the longest line / single lines
    }

    [Fact]
    public async Task A_Book_Found_Through_One_Line_Is_Ranked_By_How_Much_It_Looks_Like_The_Whole_Spine()
    {
        var shelf = await NewShelfAsync();
        var ids = await AddJobAsync(shelf, ("Nuromanzir | William Gibson", "Nuromanzir", "William Gibson", 0.9));
        _factory.Search.ByText["William Gibson"] = new List<BookMatch>
        {
            new("9780670857784", "Idoru", "William Gibson", null, 0, "OpenLibrary"),
            new("9780441012039", "Neuromancer", "William Gibson", null, 0, "OpenLibrary"),
            new("9780670921560", "The Peripheral", "William Gibson", null, 0, "OpenLibrary")
        };

        var json = await GetJsonAsync($"/api/shelves/{shelf}/spines/{ids[0]}/suggestions");

        var titles = json.GetProperty("suggestions").EnumerateArray().Select(x => x.GetProperty("title").GetString()).ToList();
        Assert.Equal("Neuromancer", titles[0]);
        Assert.Equal(3, titles.Count);
    }

    [Fact]
    public void Strategies_Are_Full_Parsed_Longest_Swapped_Then_Lines_Without_Repeats()
    {
        var s = Shelfy.Features.Recognition.FixSpine.SpineSuggestionsHandler.Strategies("Asimov Isaac | THE FOUNDATION TRILOGY | Bantam", "Asimov Isaac", "THE FOUNDATION TRILOGY");

        Assert.Equal(new[] { "full", "parsed", "longest", "swapped", "line" }, s.Select(x => x.Name).Take(5));
        Assert.Equal("Asimov Isaac THE FOUNDATION TRILOGY Bantam", s[0].Probe);
        Assert.Equal("THE FOUNDATION TRILOGY", s[2].Probe);
        Assert.Equal("THE FOUNDATION TRILOGY", s[3].Structured!.Title);   // the parsed guess the wrong way round
        Assert.Equal("Asimov Isaac", s[3].Structured!.Author);
        Assert.Equal(s.Count, s.Select(x => x.Name + x.Probe).Distinct().Count());
    }

    [Fact]
    public async Task Suggestions_Stop_Early_Once_Five_Good_Books_Were_Found()
    {
        var shelf = await NewShelfAsync();
        var ids = await AddJobAsync(shelf, ("Dune | Frank Herbert", "Dune", "Frank Herbert", 0.9));
        _factory.Search.ByText["Dune Frank Herbert"] = Enumerable.Range(1, 6)
            .Select(i => new BookMatch($"97800000000{i:00}", "Dune", "Frank Herbert", null, 0, "OpenLibrary")).ToList();
        // distinct ISBNs: validity is not checked for catalog answers

        var json = await GetJsonAsync($"/api/shelves/{shelf}/spines/{ids[0]}/suggestions");

        Assert.Equal(5, json.GetProperty("suggestions").GetArrayLength());
        Assert.Single(_factory.Search.Asked);
    }

    [Fact]
    public async Task Suggestions_Say_Unavailable_When_The_Catalogs_Are_Down_And_Never_Fail()
    {
        var shelf = await NewShelfAsync();
        var ids = await AddJobAsync(shelf, ("Hyperion | Dan Simmons", "Hyperion", "Dan Simmons", 0.9));
        _factory.Search.Throw = new RecognitionException("OpenLibrary returned 429.", isRetryable: true);

        var json = await GetJsonAsync($"/api/shelves/{shelf}/spines/{ids[0]}/suggestions");

        Assert.True(json.GetProperty("unavailable").GetBoolean());
        Assert.Equal(0, json.GetProperty("suggestions").GetArrayLength());
    }

    [Fact]
    public async Task The_Search_Box_Finds_Books_Already_Here_And_In_The_Catalogs_Once_Each()
    {
        var shelf = await NewShelfAsync();
        var ids = await AddJobAsync(shelf, ("zzz qqq", "zzz", null, 0.9));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
            db.Books.Add(new Book { Id = Guid.NewGuid(), Isbn = "9780441172719", Title = "Dune", Author = "Frank Herbert" });
            await db.SaveChangesAsync();
        }

        _factory.Search.ByText["dune herbert"] = new List<BookMatch>
        {
            new("9780441172719", "Dune", "Frank Herbert", null, 0, "OpenLibrary"),          // already known: not listed twice
            new("9780441172696", "Dune Messiah", "Frank Herbert", null, 0, "OpenLibrary")
        };

        var json = await GetJsonAsync($"/api/shelves/{shelf}/spines/{ids[0]}/suggestions?q=dune%20herbert");

        var s = json.GetProperty("suggestions").EnumerateArray().ToList();
        Assert.Equal(new[] { "Dune", "Dune Messiah" }, s.Select(x => x.GetProperty("title").GetString()));
        Assert.Equal("Shelfy", s[0].GetProperty("source").GetString());
        Assert.NotEqual(JsonValueKind.Null, s[0].GetProperty("bookId").ValueKind);
        Assert.All(s, x => Assert.Equal("search", x.GetProperty("strategy").GetString()));

        // An ISBN typed in finds the known book without asking anybody for words.
        var byIsbn = await GetJsonAsync($"/api/shelves/{shelf}/spines/{ids[0]}/suggestions?q=978-0-441-17271-9");
        Assert.Equal("Dune", byIsbn.GetProperty("suggestions")[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Suggestions_Are_404_For_Unknown_Hidden_Or_Foreign_Spines()
    {
        var shelf = await NewShelfAsync();
        var other = await NewShelfAsync();
        var ids = await AddJobAsync(shelf, ("Hyperion | Dan Simmons", "Hyperion", "Dan Simmons", 0.9));

        await GetJsonAsync($"/api/shelves/{shelf}/spines/{Guid.NewGuid()}/suggestions", HttpStatusCode.NotFound);
        await GetJsonAsync($"/api/shelves/{other}/spines/{ids[0]}/suggestions", HttpStatusCode.NotFound);     // another shelf's spine
        await GetJsonAsync($"/api/shelves/{Guid.NewGuid()}/spines/{ids[0]}/suggestions", HttpStatusCode.NotFound);

        await _client.PostAsync($"/api/shelves/{shelf}/hide", null);
        await GetJsonAsync($"/api/shelves/{shelf}/spines/{ids[0]}/suggestions", HttpStatusCode.NotFound);
    }

    // ---- resolve ----

    [Fact]
    public async Task Resolve_Adds_The_Book_As_Private_Removes_The_Spine_From_The_List_And_Marks_It_As_Chosen_By_The_User()
    {
        var shelf = await NewShelfAsync();
        var ids = await AddJobAsync(shelf, ("Foundaton | Isaac Asimov", "Foundaton", "Isaac Asimov", 0.9), ("Hyperion Dan Simmons", "Hyperion", "Dan Simmons", 0.8));

        var (status, body) = await PostAsync($"/api/shelves/{shelf}/spines/{ids[0]}/resolve", Foundation);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("addedToShelf").GetBoolean());
        Assert.Equal("Foundation", body.GetProperty("book").GetProperty("title").GetString());
        Assert.Equal(FoundationIsbn, body.GetProperty("book").GetProperty("isbn").GetString());
        Assert.True(body.GetProperty("remembered").GetBoolean());
        Assert.Equal(new[] { "Hyperion Dan Simmons" }, await UnmatchedAsync(shelf));
        Assert.Equal(new[] { "Foundation" }, await ShelfTitlesAsync(shelf));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
        var spine = await db.DetectedSpines.Include(s => s.Book).SingleAsync(s => s.Id == ids[0]);
        Assert.Equal(SpineResolutionStatus.Resolved, spine.Status);
        Assert.Equal("User", spine.MatchSource);
        Assert.Equal(1.0, spine.MatchScore);
        Assert.Equal(FoundationIsbn, spine.ResolvedIsbn);
        Assert.Equal(AvailabilityStatus.Private, (await db.ShelfBooks.SingleAsync()).Status);
    }

    [Fact]
    public async Task Resolve_Twice_Is_Fine_And_Adds_Nothing_Twice()
    {
        var shelf = await NewShelfAsync();
        var ids = await AddJobAsync(shelf, ("Foundaton | Isaac Asimov", "Foundaton", "Isaac Asimov", 0.9));

        var first = await PostAsync($"/api/shelves/{shelf}/spines/{ids[0]}/resolve", Foundation);
        var second = await PostAsync($"/api/shelves/{shelf}/spines/{ids[0]}/resolve", Foundation);

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.False(second.Body.GetProperty("addedToShelf").GetBoolean());
        Assert.Equal(first.Body.GetProperty("book").GetProperty("bookId").GetGuid(), second.Body.GetProperty("book").GetProperty("bookId").GetGuid());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
        Assert.Equal(1, await db.ShelfBooks.CountAsync());
        Assert.Equal(1, await db.Books.CountAsync(b => b.Isbn == FoundationIsbn));
    }

    [Fact]
    public async Task Resolve_To_A_Book_Already_On_The_Shelf_Does_Not_Add_A_Duplicate_And_An_Existing_Book_Is_Reused_By_Isbn()
    {
        var shelf = await NewShelfAsync();
        var ids = await AddJobAsync(shelf, ("Foundaton | Isaac Asimov", "Foundaton", "Isaac Asimov", 0.9), ("Foundation Asimov", "Foundation", "Asimov", 0.7));
        Guid bookId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
            var book = new Book { Id = Guid.NewGuid(), Isbn = FoundationIsbn, Title = "Foundation", Author = "Isaac Asimov" };
            db.Books.Add(book);
            db.Shelves.Include(s => s.Books).Single(s => s.Id == shelf).AddBook(book.Id);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var (status, body) = await PostAsync($"/api/shelves/{shelf}/spines/{ids[1]}/resolve",
            new { isbn13 = FoundationIsbn, title = "A DIFFERENT TITLE", author = "x" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("addedToShelf").GetBoolean());
        Assert.Equal(bookId, body.GetProperty("book").GetProperty("bookId").GetGuid());
        Assert.Equal("Foundation", body.GetProperty("book").GetProperty("title").GetString());   // the stored book wins over client details
        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ShelfyDbContext>();
        Assert.Equal(1, await db2.ShelfBooks.CountAsync());
        Assert.Equal(1, await db2.Books.CountAsync());
    }

    [Fact]
    public async Task Resolve_Marks_Every_Read_Of_The_Same_Text_On_The_Shelf_But_Not_On_Other_Shelves()
    {
        var shelf = await NewShelfAsync();
        var other = await NewShelfAsync();
        var a = await AddJobAsync(shelf, ("Foundaton | Isaac Asimov", "Foundaton", "Isaac Asimov", 0.9));
        var b = await AddJobAsync(shelf, ("FOUNDATON - isaac asimov", "Foundaton", "Isaac Asimov", 0.8), ("Hyperion Dan Simmons", "Hyperion", "Dan Simmons", 0.8));
        var o = await AddJobAsync(other, ("Foundaton | Isaac Asimov", "Foundaton", "Isaac Asimov", 0.9));

        var (_, body) = await PostAsync($"/api/shelves/{shelf}/spines/{a[0]}/resolve", Foundation);

        Assert.Equal(2, body.GetProperty("spinesResolved").GetInt32());
        Assert.Equal(new[] { "Hyperion Dan Simmons" }, await UnmatchedAsync(shelf));
        Assert.Equal(new[] { "Foundaton | Isaac Asimov" }, await UnmatchedAsync(other));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
        Assert.NotNull((await db.DetectedSpines.SingleAsync(s => s.Id == b[0])).BookId);
        Assert.Null((await db.DetectedSpines.SingleAsync(s => s.Id == o[0])).BookId);
    }

    [Fact]
    public async Task Resolve_A_Different_Book_For_An_Already_Matched_Spine_Is_409_And_Bad_Requests_Are_400_Or_404()
    {
        var shelf = await NewShelfAsync();
        var ids = await AddJobAsync(shelf, ("Foundaton | Isaac Asimov", "Foundaton", "Isaac Asimov", 0.9));
        await PostAsync($"/api/shelves/{shelf}/spines/{ids[0]}/resolve", Foundation);

        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync($"/api/shelves/{shelf}/spines/{ids[0]}/resolve",
            new { isbn13 = "9780441172719", title = "Dune", author = "Frank Herbert" })).Status);

        var fresh = await AddJobAsync(shelf, ("Hyperion Dan Simmons", "Hyperion", "Dan Simmons", 0.8));
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync($"/api/shelves/{shelf}/spines/{fresh[0]}/resolve", new { isbn13 = "12345" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync($"/api/shelves/{shelf}/spines/{fresh[0]}/resolve", new { })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync($"/api/shelves/{shelf}/spines/{fresh[0]}/resolve", new { isbn13 = "9780441172719" })).Status);   // new book, no title
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync($"/api/shelves/{shelf}/spines/{fresh[0]}/resolve", new { bookId = Guid.NewGuid() })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync($"/api/shelves/{shelf}/spines/{Guid.NewGuid()}/resolve", Foundation)).Status);
        Assert.Equal(new[] { "Hyperion Dan Simmons" }, await UnmatchedAsync(shelf));
    }

    [Fact]
    public async Task Resolve_By_Known_Book_Id_Works_And_A_Hidden_Shelf_Cannot_Be_Fixed()
    {
        var shelf = await NewShelfAsync();
        var ids = await AddJobAsync(shelf, ("Hyperion Dan Simmons", "Hyperion", "Dan Simmons", 0.8));
        Guid bookId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
            var book = new Book { Id = Guid.NewGuid(), Isbn = "9780553283686", Title = "Hyperion", Author = "Dan Simmons" };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            bookId = book.Id;
        }

        var (status, body) = await PostAsync($"/api/shelves/{shelf}/spines/{ids[0]}/resolve", new { bookId });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new[] { "Hyperion" }, await ShelfTitlesAsync(shelf));
        Assert.Equal(bookId, body.GetProperty("book").GetProperty("bookId").GetGuid());

        var hidden = await NewShelfAsync();
        var h = await AddJobAsync(hidden, ("Dune Frank Herbert", "Dune", "Frank Herbert", 0.9));
        await _client.PostAsync($"/api/shelves/{hidden}/hide", null);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync($"/api/shelves/{hidden}/spines/{h[0]}/resolve", Foundation)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync($"/api/shelves/{hidden}/spines/{h[0]}/dismiss")).Status);
        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ShelfyDbContext>();
        Assert.Equal(0, await db2.ShelfBooks.CountAsync(sb => sb.ShelfId == hidden));
        Assert.False((await db2.DetectedSpines.SingleAsync(s => s.Id == h[0])).Dismissed);
    }

    // ---- dismiss ----

    [Fact]
    public async Task Dismiss_Hides_The_Spine_And_Its_Repeats_Keeps_The_Rows_And_Is_Idempotent()
    {
        var shelf = await NewShelfAsync();
        var a = await AddJobAsync(shelf, ("Spine Of Nothing Useful", "Spine Of Nothing Useful", null, 0.9), ("Hyperion Dan Simmons", "Hyperion", "Dan Simmons", 0.8));
        await AddJobAsync(shelf, ("SPINE OF NOTHING USEFUL", "Spine", null, 0.8));

        var first = await PostAsync($"/api/shelves/{shelf}/spines/{a[0]}/dismiss");
        var second = await PostAsync($"/api/shelves/{shelf}/spines/{a[0]}/dismiss");

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.True(first.Body.GetProperty("dismissed").GetBoolean());
        Assert.Equal(2, first.Body.GetProperty("spinesDismissed").GetInt32());
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(0, second.Body.GetProperty("spinesDismissed").GetInt32());
        Assert.Equal(new[] { "Hyperion Dan Simmons" }, await UnmatchedAsync(shelf));
        Assert.Equal(new[] { "Hyperion Dan Simmons" }, await UnmatchedAsync(shelf, includeHidden: true));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
        Assert.Equal(3, await db.DetectedSpines.CountAsync());           // nothing was deleted
        Assert.Equal(0, await db.UserCorrections.CountAsync());          // and nothing is remembered
    }

    [Fact]
    public async Task Dismissing_A_Matched_Spine_Is_409_And_An_Unknown_One_404()
    {
        var shelf = await NewShelfAsync();
        var ids = await AddJobAsync(shelf, ("Foundaton | Isaac Asimov", "Foundaton", "Isaac Asimov", 0.9));
        await PostAsync($"/api/shelves/{shelf}/spines/{ids[0]}/resolve", Foundation);

        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync($"/api/shelves/{shelf}/spines/{ids[0]}/dismiss")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync($"/api/shelves/{shelf}/spines/{Guid.NewGuid()}/dismiss")).Status);
    }

    [Fact]
    public async Task A_Dismissed_Spine_Can_Still_Be_Fixed_Afterwards()
    {
        var shelf = await NewShelfAsync();
        var ids = await AddJobAsync(shelf, ("Foundaton | Isaac Asimov", "Foundaton", "Isaac Asimov", 0.9));
        await PostAsync($"/api/shelves/{shelf}/spines/{ids[0]}/dismiss");

        var (status, _) = await PostAsync($"/api/shelves/{shelf}/spines/{ids[0]}/resolve", Foundation);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new[] { "Foundation" }, await ShelfTitlesAsync(shelf));
    }

    // ---- batch endpoint (the list while photos are being read) ----

    [Fact]
    public async Task The_Batch_Lists_Spine_Ids_Flags_Noise_Shows_Fixed_Spines_As_Books_And_Leaves_Out_Dismissed_Ones()
    {
        var shelf = await NewShelfAsync();
        var batch = Guid.NewGuid();
        List<Guid> ids;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
            var job = RecognitionJob.CreatePending(shelf, "k" + Guid.NewGuid(), "image/jpeg", 1, Guid.NewGuid().ToString("N"), 4, DateTime.UtcNow, batch, 1, 1);
            job.Status = RecognitionJobStatus.Completed;
            db.RecognitionJobs.Add(job);
            ids = new List<Guid>();
            var i = 0;
            foreach (var (raw, conf) in new[] { ("Foundaton | Isaac Asimov", 0.9), ("Hyperion Dan Simmons", 0.8), ("TH", 0.5), ("Not A Book At All", 0.9) })
            {
                var s = new DetectedSpine { Id = Guid.NewGuid(), RecognitionJobId = job.Id, Position = i++, RawText = raw, CandidateTitle = raw, Confidence = conf };
                db.DetectedSpines.Add(s);
                ids.Add(s.Id);
            }

            await db.SaveChangesAsync();
        }

        await PostAsync($"/api/shelves/{shelf}/spines/{ids[0]}/resolve", Foundation);
        await PostAsync($"/api/shelves/{shelf}/spines/{ids[3]}/dismiss");

        var json = await GetJsonAsync($"/api/shelves/{shelf}/batches/{batch}");

        var spines = json.GetProperty("spines").EnumerateArray().ToList();
        Assert.Equal(3, spines.Count);                                                    // dismissed one is gone
        Assert.Equal("Foundation", spines[0].GetProperty("book").GetProperty("title").GetString());
        Assert.Equal("User", spines[0].GetProperty("matchSource").GetString());
        Assert.Equal(ids[1], spines[1].GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, spines[1].GetProperty("noiseReason").ValueKind);
        Assert.Equal("short", spines[2].GetProperty("noiseReason").GetString());
    }

    // ---- remembered corrections ----

    [Fact]
    public async Task A_Fix_Is_Remembered_So_The_Same_Text_Matches_On_Another_Shelf_Without_Asking_The_Catalogs()
    {
        // Shelf A: the canned photo reads Foundation, which the fake catalog does not know -> unmatched.
        var a = await NewShelfAsync();
        var jobA = await UploadAsync(a, 1);
        await _factory.RunWorkerOnceAsync();
        var unmatched = await GetJsonAsync($"/api/shelves/{a}/unmatched");
        var spine = unmatched.GetProperty("spines").EnumerateArray().Single(s => s.GetProperty("text").GetString()!.Contains("Foundation"));

        var (status, body) = await PostAsync($"/api/shelves/{a}/spines/{spine.GetProperty("id").GetGuid()}/resolve", Foundation);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("remembered").GetBoolean());

        // Shelf B: a different photo with the same spine text.
        _factory.Resolver.Queries.Clear();
        var b = await NewShelfAsync();
        var jobB = await UploadAsync(b, 2);
        await _factory.RunWorkerOnceAsync();

        Assert.Equal(new[] { "Dune", "Foundation", "The Hobbit" }, (await ShelfTitlesAsync(b)).OrderBy(t => t));
        Assert.DoesNotContain(_factory.Resolver.Queries, q => q.Title == "Foundation");       // never went to the catalogs
        Assert.Empty(await UnmatchedAsync(b));
        var job = await GetJsonAsync($"/api/recognition-jobs/{jobB}");
        var foundation = job.GetProperty("spines").EnumerateArray().Single(s => s.GetProperty("book").ValueKind == JsonValueKind.Object && s.GetProperty("book").GetProperty("title").GetString() == "Foundation");
        Assert.Equal("Memory", foundation.GetProperty("matchSource").GetString());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
        Assert.Equal(1, await db.UserCorrections.SumAsync(c => c.UseCount));
        Assert.Equal(1, await db.Books.CountAsync(x => x.Isbn == FoundationIsbn));
    }

    [Fact]
    public async Task Changing_Your_Mind_Replaces_The_Remembered_Book()
    {
        var shelf = await NewShelfAsync();
        var one = await AddJobAsync(shelf, ("Zorblax Quentin | Mystery Writer", "Zorblax Quentin", "Mystery Writer", 0.9));
        await PostAsync($"/api/shelves/{shelf}/spines/{one[0]}/resolve", Foundation);
        var other = await NewShelfAsync();
        var two = await AddJobAsync(other, ("Zorblax Quentin | Mystery Writer", "Zorblax Quentin", "Mystery Writer", 0.9));

        await PostAsync($"/api/shelves/{other}/spines/{two[0]}/resolve", new { isbn13 = "9780441172719", title = "Dune", author = "Frank Herbert" });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
        var rows = await db.UserCorrections.Include(c => c.Book).ToListAsync();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("9780441172719", r.Book!.Isbn));
        Assert.Equal(rows.Count, rows.Select(r => r.TextKey).Distinct().Count());               // one row per key
    }

    private async Task<Guid> UploadAsync(Guid shelfId, int variant)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(TestImages.Jpeg(1000 + variant));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "shelf.jpg");
        var response = await _client.PostAsync($"/api/shelves/{shelfId}/photos", form);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("jobId").GetGuid();
    }
}
