using Shelfy.Features.Recognition;
using Shelfy.Features.Recognition.Resolution;
using Shelfy.Tests.Support;

namespace Shelfy.Tests.Recognition;

/// <summary>The multi-result catalog search behind the tap-to-fix sheet (ADR-011).</summary>
public class BookSearchTests
{
    private static HttpClient Client(HttpMessageHandler handler, string baseAddress) => new(handler) { BaseAddress = new Uri(baseAddress) };

    [Fact]
    public async Task OpenLibrary_Free_Text_Uses_The_Q_Parameter_And_Returns_Every_Book_With_An_Isbn()
    {
        var stub = StubHandler.Always(Fixtures.Read("OpenLibrary/search-the-hobbit.json"));
        var search = new OpenLibraryBookResolver(Client(stub, "https://openlibrary.org/"));

        var found = await search.SearchTextAsync("the hobbit tolkien", 8);

        Assert.NotEmpty(found);
        Assert.All(found, m => Assert.Equal(13, m.Isbn.Length));
        Assert.Contains("q=the%20hobbit%20tolkien", stub.Requests.Single().RequestUri!.AbsoluteUri);
        Assert.DoesNotContain("title=", stub.Requests.Single().RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task OpenLibrary_Free_Text_That_Is_An_Isbn_Asks_By_Isbn_And_Reports_That_Isbn()
    {
        var stub = StubHandler.Always(Fixtures.Read("OpenLibrary/search-the-hobbit.json"));
        var search = new OpenLibraryBookResolver(Client(stub, "https://openlibrary.org/"));

        var found = await search.SearchTextAsync("978-0-547-92822-7", 5);

        Assert.Contains("q=isbn%3A9780547928227", stub.Requests.Single().RequestUri!.AbsoluteUri);
        Assert.All(found, m => Assert.Equal("9780547928227", m.Isbn));
    }

    [Fact]
    public async Task OpenLibrary_Structured_Search_Scores_Each_Candidate_Against_The_Query()
    {
        var stub = StubHandler.Always(Fixtures.Read("OpenLibrary/search-the-hobbit.json"));
        var search = new OpenLibraryBookResolver(Client(stub, "https://openlibrary.org/"));

        var found = await search.SearchAsync(new BookQuery("The Hobbit", "J.R.R. Tolkien"), 8);

        Assert.True(found.Max(m => m.Score) > 0.95);
        Assert.Contains("title=The%20Hobbit", stub.Requests.Single().RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Google_Free_Text_Searches_The_Plain_Text()
    {
        var stub = StubHandler.Always(Fixtures.Read("GoogleBooks/volumes-foundation.json"));
        var search = new GoogleBooksResolver(Client(stub, "https://www.googleapis.com/"));

        var found = await search.SearchTextAsync("foundation asimov", 8);

        Assert.NotEmpty(found);
        Assert.Contains("q=foundation%20asimov", stub.Requests.Single().RequestUri!.AbsoluteUri);
    }

    private sealed class Provider(string name, Func<Task<IReadOnlyList<BookMatch>>> answer) : IBookSearch
    {
        public int Calls { get; private set; }
        public string Name => name;
        public Task<IReadOnlyList<BookMatch>> SearchAsync(BookQuery query, int limit, CancellationToken ct = default) { Calls++; return answer(); }
        public Task<IReadOnlyList<BookMatch>> SearchTextAsync(string text, int limit, CancellationToken ct = default) { Calls++; return answer(); }
    }

    private static Task<IReadOnlyList<BookMatch>> Books(params string[] isbns) =>
        Task.FromResult<IReadOnlyList<BookMatch>>(isbns.Select(i => new BookMatch(i, "T" + i, "A", null, 0, "X")).ToList());

    [Fact]
    public async Task Composite_Stops_Asking_Once_It_Has_Enough_And_Merges_By_Isbn()
    {
        var first = new Provider("one", () => Books("1", "2"));
        var second = new Provider("two", () => Books("2", "3"));
        var composite = new CompositeBookSearch(new IBookSearch[] { first, second });

        var enough = await composite.SearchTextAsync("x", 2);
        Assert.Equal(2, enough.Count);
        Assert.Equal(0, second.Calls);

        var more = await composite.SearchTextAsync("x", 5);
        Assert.Equal(new[] { "1", "2", "3" }, more.Select(m => m.Isbn).OrderBy(i => i));
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public async Task Composite_Skips_A_Provider_That_Is_Down_And_Throws_Only_When_All_Are()
    {
        var down = new Provider("down", () => throw new RecognitionException("429", isRetryable: true));
        var up = new Provider("up", () => Books("9"));

        Assert.Single(await new CompositeBookSearch(new IBookSearch[] { down, up }).SearchTextAsync("x", 5));

        var ex = await Assert.ThrowsAsync<RecognitionException>(() =>
            new CompositeBookSearch(new IBookSearch[] { down, down }).SearchTextAsync("x", 5));
        Assert.True(ex.IsRetryable);
    }
}
