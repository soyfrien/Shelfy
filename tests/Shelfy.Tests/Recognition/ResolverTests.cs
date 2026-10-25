using System.Net;
using Microsoft.Extensions.Options;
using Shelfy.Features.Recognition;
using Shelfy.Features.Recognition.Resolution;
using Shelfy.Tests.Support;

namespace Shelfy.Tests.Recognition;

public class ResolverTests
{
    private static HttpClient Client(HttpMessageHandler handler, string baseAddress) =>
        new(handler) { BaseAddress = new Uri(baseAddress) };

    private static HttpClient WithRetry(StubHandler stub, int retries, List<TimeSpan>? delays = null, string baseAddress = "https://openlibrary.org/")
    {
        var retry = new RetryHandler(retries, TimeSpan.FromMilliseconds(100), (d, _) =>
        {
            delays?.Add(d);
            return Task.CompletedTask;
        })
        { InnerHandler = stub };
        return Client(retry, baseAddress);
    }

    // ---- OpenLibrary ----

    [Fact]
    public async Task OpenLibrary_Picks_The_Best_Scoring_Doc_That_Has_An_Isbn()
    {
        var stub = StubHandler.Always(Fixtures.Read("OpenLibrary/search-the-hobbit.json"));
        var resolver = new OpenLibraryBookResolver(Client(stub, "https://openlibrary.org/"));

        var match = await resolver.ResolveAsync(new BookQuery("The Hobbit", "J.R.R. Tolkien"));

        Assert.NotNull(match);
        Assert.Equal("9780547928227", match!.Isbn); // first valid ISBN of the work, normalized to ISBN-13
        Assert.Equal("The Hobbit", match.Title);
        Assert.Equal("J.R.R. Tolkien", match.Author);
        Assert.Equal("https://covers.openlibrary.org/b/id/8406786-M.jpg", match.CoverUrl);
        Assert.Equal("OpenLibrary", match.Source);
        Assert.True(match.Score > 0.95);
    }

    [Fact]
    public async Task OpenLibrary_Sends_Title_And_Author_As_Encoded_Query_Parameters()
    {
        var stub = StubHandler.Always(Fixtures.Read("OpenLibrary/search-empty.json"));
        var resolver = new OpenLibraryBookResolver(Client(stub, "https://openlibrary.org/"));

        await resolver.ResolveAsync(new BookQuery("Who Moved My Cheese?", "Spencer Johnson"));

        var uri = stub.Requests.Single().RequestUri!;
        Assert.Equal("openlibrary.org", uri.Host);
        Assert.Equal("/search.json", uri.AbsolutePath);
        Assert.Contains("title=Who%20Moved%20My%20Cheese%3F", uri.AbsoluteUri);
        Assert.Contains("author=Spencer%20Johnson", uri.AbsoluteUri);
    }

    [Fact]
    public async Task OpenLibrary_Returns_Null_When_Nothing_Is_Found()
    {
        var resolver = new OpenLibraryBookResolver(Client(StubHandler.Always(Fixtures.Read("OpenLibrary/search-empty.json")), "https://openlibrary.org/"));

        Assert.Null(await resolver.ResolveAsync(new BookQuery("Nonexistent Book Title", "Nobody")));
    }

    [Fact]
    public async Task OpenLibrary_Ignores_Docs_Without_An_Isbn()
    {
        var resolver = new OpenLibraryBookResolver(Client(StubHandler.Always(Fixtures.Read("OpenLibrary/search-no-isbn.json")), "https://openlibrary.org/"));

        Assert.Null(await resolver.ResolveAsync(new BookQuery("Obscure Pamphlet", "A. Nonymous")));
    }

    [Fact]
    public async Task OpenLibrary_Looks_Up_By_Isbn_When_One_Was_Read()
    {
        var stub = StubHandler.Always(Fixtures.Read("OpenLibrary/search-dune.json"));
        var resolver = new OpenLibraryBookResolver(Client(stub, "https://openlibrary.org/"));

        var match = await resolver.ResolveAsync(new BookQuery("", null, "9780441172719"));

        Assert.Equal(1.0, match!.Score);
        Assert.Equal("9780441172719", match.Isbn);
        Assert.Contains("q=isbn:9780441172719", stub.Requests.Single().RequestUri!.Query);
    }

    [Fact]
    public async Task OpenLibrary_Server_Error_After_Retries_Is_A_Retryable_Failure()
    {
        var stub = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var resolver = new OpenLibraryBookResolver(WithRetry(stub, retries: 2));

        var ex = await Assert.ThrowsAsync<RecognitionException>(() => resolver.ResolveAsync(new BookQuery("Dune", null)));

        Assert.True(ex.IsRetryable);
        Assert.Equal(3, stub.Requests.Count); // first try + 2 retries
    }

    [Fact]
    public async Task OpenLibrary_Bad_Request_Means_No_Result_Not_A_Failure()
    {
        var resolver = new OpenLibraryBookResolver(Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)), "https://openlibrary.org/"));

        Assert.Null(await resolver.ResolveAsync(new BookQuery("Dune", null)));
    }

    // ---- Google Books ----

    [Fact]
    public async Task GoogleBooks_Maps_Isbn13_Cover_And_Authors()
    {
        var stub = StubHandler.Always(Fixtures.Read("GoogleBooks/volumes-foundation.json"));
        var resolver = new GoogleBooksResolver(Client(stub, "https://www.googleapis.com/"));

        var match = await resolver.ResolveAsync(new BookQuery("Foundation", "Isaac Asimov"));

        Assert.NotNull(match);
        Assert.Equal("9780553293357", match!.Isbn);
        Assert.Equal("Isaac Asimov", match.Author);
        Assert.StartsWith("https://books.google.com/", match.CoverUrl); // upgraded to https
        Assert.DoesNotContain("edge=curl", match.CoverUrl);
        Assert.Equal("GoogleBooks", match.Source);
        Assert.True(match.Score > 0.95); // "Foundation and Empire" scores lower
    }

    [Fact]
    public async Task GoogleBooks_Query_Uses_Intitle_And_Inauthor_And_The_Optional_Key()
    {
        var stub = StubHandler.Always(Fixtures.Read("GoogleBooks/volumes-empty.json"));
        var resolver = new GoogleBooksResolver(Client(stub, "https://www.googleapis.com/"), apiKey: "test-key");

        Assert.Null(await resolver.ResolveAsync(new BookQuery("Foundation", "Isaac Asimov")));

        var uri = stub.Requests.Single().RequestUri!.AbsoluteUri;
        Assert.Contains("/books/v1/volumes?q=intitle%3AFoundation%20inauthor%3AIsaac%20Asimov", uri);
        Assert.Contains("key=test-key", uri);
    }

    [Fact]
    public async Task GoogleBooks_Omits_The_Key_When_None_Is_Configured()
    {
        var stub = StubHandler.Always(Fixtures.Read("GoogleBooks/volumes-empty.json"));

        await new GoogleBooksResolver(Client(stub, "https://www.googleapis.com/")).ResolveAsync(new BookQuery("Foundation"));

        Assert.DoesNotContain("key=", stub.Requests.Single().RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GoogleBooks_Quota_Errors_Are_Retryable()
    {
        var resolver = new GoogleBooksResolver(Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)), "https://www.googleapis.com/"));

        var ex = await Assert.ThrowsAsync<RecognitionException>(() => resolver.ResolveAsync(new BookQuery("Dune")));

        Assert.True(ex.IsRetryable);
    }

    [Fact]
    public async Task Invalid_Json_Is_A_Retryable_Failure()
    {
        var resolver = new OpenLibraryBookResolver(Client(StubHandler.Always("<html>oops</html>"), "https://openlibrary.org/"));

        var ex = await Assert.ThrowsAsync<RecognitionException>(() => resolver.ResolveAsync(new BookQuery("Dune")));

        Assert.True(ex.IsRetryable);
    }

    // ---- Composite ----

    private static CompositeBookResolver Composite(params IBookResolver[] resolvers) =>
        new(resolvers, Options.Create(new RecognitionOptions { MinMatchScore = 0.72 }));

    [Fact]
    public async Task Composite_Uses_OpenLibrary_First_And_Skips_Google_When_It_Matches()
    {
        var google = new FakeBookResolver();
        google.Matches["Dune"] = FakeBookResolver.Match("9780441172719", "Dune", "Frank Herbert");
        var ol = new OpenLibraryBookResolver(Client(StubHandler.Always(Fixtures.Read("OpenLibrary/search-dune.json")), "https://openlibrary.org/"));

        var match = await Composite(ol, google).ResolveAsync(new BookQuery("Dune", "Frank Herbert"));

        Assert.Equal("OpenLibrary", match!.Source);
        Assert.Empty(google.Queries);
    }

    [Fact]
    public async Task Composite_Falls_Back_To_Google_Books_When_OpenLibrary_Finds_Nothing()
    {
        var ol = new OpenLibraryBookResolver(Client(StubHandler.Always(Fixtures.Read("OpenLibrary/search-empty.json")), "https://openlibrary.org/"));
        var gb = new GoogleBooksResolver(Client(StubHandler.Always(Fixtures.Read("GoogleBooks/volumes-foundation.json")), "https://www.googleapis.com/"));

        var match = await Composite(ol, gb).ResolveAsync(new BookQuery("Foundation", "Isaac Asimov"));

        Assert.Equal("GoogleBooks", match!.Source);
        Assert.Equal("9780553293357", match.Isbn);
    }

    [Fact]
    public async Task Composite_Falls_Back_When_OpenLibrary_Is_Down()
    {
        var down = new OpenLibraryBookResolver(Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)), "https://openlibrary.org/"));
        var gb = new GoogleBooksResolver(Client(StubHandler.Always(Fixtures.Read("GoogleBooks/volumes-foundation.json")), "https://www.googleapis.com/"));

        var match = await Composite(down, gb).ResolveAsync(new BookQuery("Foundation", "Isaac Asimov"));

        Assert.Equal("GoogleBooks", match!.Source);
    }

    [Fact]
    public async Task Composite_Throws_Retryable_When_Every_Provider_Is_Down()
    {
        var down1 = new OpenLibraryBookResolver(Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)), "https://openlibrary.org/"));
        var down2 = new GoogleBooksResolver(Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)), "https://www.googleapis.com/"));

        var ex = await Assert.ThrowsAsync<RecognitionException>(() => Composite(down1, down2).ResolveAsync(new BookQuery("Dune")));

        Assert.True(ex.IsRetryable);
    }

    [Fact]
    public async Task Composite_Rejects_Matches_Below_The_Threshold()
    {
        var fake = new FakeBookResolver();
        fake.Matches["Dune"] = FakeBookResolver.Match("9780441172719", "Something Else", "Someone", score: 0.5);

        Assert.Null(await Composite(fake).ResolveAsync(new BookQuery("Dune", "Frank Herbert")));
    }

    [Fact]
    public async Task Composite_Returns_Null_When_Providers_Simply_Find_Nothing()
    {
        var ol = new OpenLibraryBookResolver(Client(StubHandler.Always(Fixtures.Read("OpenLibrary/search-empty.json")), "https://openlibrary.org/"));
        var gb = new GoogleBooksResolver(Client(StubHandler.Always(Fixtures.Read("GoogleBooks/volumes-empty.json")), "https://www.googleapis.com/"));

        Assert.Null(await Composite(ol, gb).ResolveAsync(new BookQuery("zzzz qqqq")));
    }

    // ---- RetryHandler ----

    [Fact]
    public async Task Retry_Succeeds_After_Transient_Errors_With_Exponential_Backoff()
    {
        var delays = new List<TimeSpan>();
        var stub = StubHandler.Sequence(
            () => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            () => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            () => StubHandler.Json("{}"));
        var client = WithRetry(stub, retries: 3, delays);

        var response = await client.GetAsync("x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, stub.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200) }, delays);
    }

    [Fact]
    public async Task Retry_Honors_Retry_After_But_Caps_It()
    {
        var delays = new List<TimeSpan>();
        var stub = StubHandler.Sequence(
            () =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3600));
                return r;
            },
            () => StubHandler.Json("{}"));

        await WithRetry(stub, 2, delays).GetAsync("x");

        Assert.Equal(new[] { TimeSpan.FromSeconds(10) }, delays);
    }

    [Fact]
    public async Task Retry_Does_Not_Retry_Client_Errors()
    {
        var stub = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var response = await WithRetry(stub, 3).GetAsync("x");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task Retry_Retries_Network_Errors()
    {
        var calls = 0;
        var stub = new StubHandler(_ => ++calls < 3 ? throw new HttpRequestException("boom") : StubHandler.Json("{}"));

        var response = await WithRetry(stub, 3).GetAsync("x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, calls);
    }
}
