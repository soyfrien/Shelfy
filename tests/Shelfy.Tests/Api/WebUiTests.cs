using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Domain;
using Shelfy.Features.Shelves.GetShelf;
using Shelfy.Features.Shelves.ShelfPage;
using Shelfy.Infrastructure;

namespace Shelfy.Tests.Api;

/// <summary>The static web UI (wwwroot) and the shareable shelf page, through the real host.</summary>
public class WebUiTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public WebUiTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Home_Page_Says_Lending_Exists_And_Offers_Sign_Up_Next_To_Sign_In()
    {
        var html = await _client.GetStringAsync("/");
        Assert.Contains("borrow and lend books with friends", html);
        var account = await _client.GetStringAsync("/account.js");
        Assert.Contains("'Forge Library card'", account);
        Assert.Contains("'#register'", account);
        Assert.Contains("to borrow, reserve, or approve requests", account);
        Assert.Contains("Waiting on ", account);
        Assert.DoesNotContain("Waiting for the owner", account);
        Assert.DoesNotContain("Owner asked for it back", account);
        var shelfJs = await _client.GetStringAsync("/shelf.js");
        Assert.Contains("isn’t lending this book (yet).", shelfJs);
        Assert.DoesNotContain("The owner isn’t lending this book (yet).", shelfJs);
        var signin = await _client.GetStringAsync("/signin.html");
        Assert.Contains("Your library card is forged!", signin);
        Assert.DoesNotContain("other person of a loan", signin);
    }

    [Fact]
    public async Task Home_Page_Waits_Long_Enough_For_Big_Shelves_And_Explains_413()
    {
        var js = await _client.GetStringAsync("/app.js");
        Assert.Contains("big shelves can take a few minutes", js);
        Assert.Contains("POLL_LIMIT_BASE_MS = 15 * 60 * 1000", js);
        Assert.Contains("e.status === 413", js);
        Assert.Contains("too large (max", js);
    }

    [Fact]
    public async Task Sample_Library_Card_Is_Public_Cacheable_And_Leads_To_Sign_Up()
    {
        var r = await _client.GetAsync("/api/card/sample.svg");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("image/svg+xml", r.Content.Headers.ContentType!.MediaType);
        Assert.Contains("public", r.Headers.CacheControl!.ToString());
        var svg = await r.Content.ReadAsStringAsync();
        Assert.Contains("Your name here", svg);
        Assert.Contains("SHELFY LIBRARY CARD", svg);
        var sampleId = Shelfy.Features.Users.LibraryCard.LibraryCardEndpoint.SampleId;
        Assert.Contains(Shelfy.Features.Users.LibraryCard.LibraryCardSvg.MemberNumber(sampleId), svg);
        Assert.Contains(Shelfy.Features.Lending.Labels.QrCode.Encode("http://localhost/signin.html#register").ToPath(0), svg);
        Assert.Equal(svg, await _client.GetStringAsync("/api/card/sample.svg"));   // the same card for everyone
        System.Xml.Linq.XDocument.Parse(svg);
    }

    [Fact]
    public async Task Library_Card_Pages_Offer_Svg_And_Png_Downloads()
    {
        foreach (var page in new[] { "/loans.html", "/signin.html" })
        {
            var html = await _client.GetStringAsync(page);
            Assert.Contains("/card.js", html);
            Assert.Contains("Download SVG", html);
            Assert.Contains("Download PNG", html);
            Assert.Contains("id=\"cardDownloadSvg\"", html);
            Assert.Contains("id=\"cardDownloadPng\"", html);
        }

        var js = await _client.GetStringAsync("/card.js");
        Assert.Contains("SCALE = 2", js);
        Assert.Contains("1712", js);
        Assert.Contains("toBlob", js);
        Assert.Contains("createObjectURL", js);
        Assert.Contains("shelfy-library-card-", js);
        Assert.Contains("image/svg+xml;charset=utf-8", js);
        Assert.Contains("Liberation Sans", js);
    }

    [Fact]
    public async Task Every_Page_Gets_The_Floating_Card_Script()
    {
        foreach (var page in new[] { "/", "/loans.html", "/signin.html", "/copy.html", "/shelf.html" })
        {
            Assert.Contains("/floatcard.js", await _client.GetStringAsync(page));
        }

        var js = await _client.GetStringAsync("/floatcard.js");
        Assert.Contains("/api/card/sample.svg", js);
        Assert.Contains("/api/account/card.svg", js);
        Assert.Contains("aria-label", js);
        Assert.Contains("prefers-reduced-motion", await _client.GetStringAsync("/style.css"));
    }

    [Fact]
    public async Task Home_Page_Is_Served_As_Html_At_The_Root()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("accept=\"image/*\"", html);
        Assert.Contains("/app.js", html);
    }

    [Fact]
    public async Task Home_Page_Has_The_Clear_Shelf_Flow_With_Hide_As_The_Default_Choice()
    {
        var html = await _client.GetStringAsync("/");

        Assert.Contains("id=\"clearBtn\"", html);
        Assert.Contains("id=\"clearModal\"", html);
        Assert.True(html.IndexOf("id=\"hideBtn\"", StringComparison.Ordinal) < html.IndexOf("id=\"deleteBtn\"", StringComparison.Ordinal),
            "the safe option comes first");
        Assert.Contains("class=\"btn danger\" id=\"deleteBtn\"", html);
        var js = await _client.GetStringAsync("/app.js");
        Assert.Contains("/hide'", js);
        Assert.Contains("api('DELETE'", js);
    }

    [Theory]
    [InlineData("/app.js", "javascript")]
    [InlineData("/shared.js", "javascript")]
    [InlineData("/search.js", "javascript")]
    [InlineData("/tiles.js", "javascript")]
    [InlineData("/shelf.js", "javascript")]
    [InlineData("/style.css", "css")]
    public async Task Static_Assets_Are_Served_And_Revalidated(string path, string type)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(type, response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("no-cache", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Health_And_Api_Still_Work_Alongside_The_Ui()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/shelves/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Guest_Can_Be_Created_And_Owns_A_Shelf_Without_Sign_In()
    {
        var guest = await _client.PostAsync("/api/guests", null);
        Assert.Equal(HttpStatusCode.Created, guest.StatusCode);
        using var guestJson = JsonDocument.Parse(await guest.Content.ReadAsStringAsync());
        var userId = guestJson.RootElement.GetProperty("id").GetGuid();

        var shelf = await _client.PostAsJsonAsync("/api/shelves", new { userId });
        Assert.Equal(HttpStatusCode.Created, shelf.StatusCode);
    }

    [Fact]
    public async Task Shelf_Page_Renders_With_Link_Preview_Tags_For_An_Existing_Shelf()
    {
        var shelfId = await SeedShelfAsync("Dune <b>&</b>", "https://covers.example/dune.jpg");

        var response = await _client.GetAsync($"/s/{shelfId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<title>A bookshelf with 1 book on Shelfy</title>", html);
        Assert.Contains("property=\"og:image\" content=\"https://covers.example/dune.jpg\"", html);
        Assert.Contains($"/s/{shelfId}", html);
        Assert.Contains("Dune &lt;b&gt;&amp;&lt;/b&gt;", html); // book data is HTML-encoded in the preview tags
        Assert.DoesNotContain("<b>&</b>", html);
        Assert.DoesNotContain("{{", html);
    }

    [Fact]
    public async Task Shelf_Page_For_An_Unknown_Shelf_Is_A_404_Html_Page()
    {
        var response = await _client.GetAsync($"/s/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("doesn't exist", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Link_Preview_Skips_Non_Http_Cover_Urls_And_Handles_An_Empty_Shelf()
    {
        const string template = "<t>{{TITLE}}|{{DESCRIPTION}}|{{URL}}</t>{{IMAGE_TAG}}";
        var id = Guid.NewGuid();
        var books = new[] { new ShelfBookDto(Guid.NewGuid(), "A", "B", null, "javascript:alert(1)", AvailabilityStatus.Private) };

        var withBad = ShelfPageEndpoint.Fill(template, new GetShelfResponse(id, Guid.NewGuid(), false, books), "https://x.test");
        var empty = ShelfPageEndpoint.Fill(template, new GetShelfResponse(id, Guid.NewGuid(), false, []), "https://x.test");

        Assert.DoesNotContain("og:image", withBad);
        Assert.Contains("A bookshelf on Shelfy", empty);
        Assert.Contains($"https://x.test/s/{id}", empty);
    }

    private async Task<Guid> SeedShelfAsync(string title, string coverUrl)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Guest" };
        var book = new Book { Id = Guid.NewGuid(), Isbn = "9780441172719", Title = title, Author = "Frank Herbert", CoverUrl = coverUrl };
        var shelf = new Shelf { Id = Guid.NewGuid(), UserId = user.Id };
        shelf.AddBook(book.Id);
        db.AddRange(user, book, shelf);
        await db.SaveChangesAsync();
        return shelf.Id;
    }
}
