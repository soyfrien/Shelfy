using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Domain;
using Shelfy.Infrastructure;

namespace Shelfy.Tests.Api;

/// <summary>GET /api/search: books on public shelves, grouped with owners and lendable copies.</summary>
public class SearchEndpointTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public SearchEndpointTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
        var louis = new User { Id = Guid.NewGuid(), DisplayName = "Louis", City = "Brooklyn" };
        db.Users.Add(louis);
        var dune = new Book { Id = Guid.NewGuid(), Title = "Dune", Author = "Frank Herbert", Isbn = "9780441172719" };
        var crash = new Book { Id = Guid.NewGuid(), Title = "Snow Crash", Author = "Neal Stephenson", Isbn = "9780553380958" };
        db.Books.AddRange(dune, crash);
        var publicShelf = new Shelf { Id = Guid.NewGuid(), UserId = louis.Id, IsPublic = true };
        publicShelf.AddBook(dune.Id);
        publicShelf.AddBook(crash.Id);
        var privateShelf = new Shelf { Id = Guid.NewGuid(), UserId = louis.Id, IsPublic = false };
        var hiddenBook = new Book { Id = Guid.NewGuid(), Title = "Private Snow", Author = "Neal Stephenson" };
        db.Books.Add(hiddenBook);
        privateShelf.AddBook(hiddenBook.Id);
        db.Shelves.AddRange(publicShelf, privateShelf);
        db.Copies.Add(Copy.Register(dune.Id, louis.Id, publicShelf.Id, "DUNE0001", DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(string.IsNullOrEmpty(text) ? "null" : text).RootElement.Clone();
    }

    [Fact]
    public async Task Finds_A_Public_Book_By_Title_Author_And_Isbn_And_Omits_Private_Shelves()
    {
        await SeedAsync();

        var byTitle = await Json(await _client.GetAsync("/api/search?q=dune"));
        Assert.Equal(1, byTitle.GetProperty("total").GetInt32());
        var book = Assert.Single(byTitle.GetProperty("books").EnumerateArray());
        Assert.Equal("Dune", book.GetProperty("title").GetString());
        Assert.Equal("Frank Herbert", book.GetProperty("author").GetString());
        Assert.Equal("9780441172719", book.GetProperty("isbn").GetString());
        Assert.True(book.GetProperty("available").GetBoolean());
        var shelf = Assert.Single(book.GetProperty("shelves").EnumerateArray());
        Assert.Equal("Louis", shelf.GetProperty("ownerName").GetString());
        Assert.True(shelf.GetProperty("available").GetBoolean());
        Assert.Equal("DUNE0001", Assert.Single(shelf.GetProperty("copies").EnumerateArray()).GetProperty("code").GetString());

        var byIsbn = await Json(await _client.GetAsync("/api/search?q=044-117-271-7"));
        Assert.Equal("Dune", Assert.Single(byIsbn.GetProperty("books").EnumerateArray()).GetProperty("title").GetString());

        var byAuthor = await Json(await _client.GetAsync("/api/search?q=Neal%20Stephenson"));
        Assert.Equal("Snow Crash", Assert.Single(byAuthor.GetProperty("books").EnumerateArray()).GetProperty("title").GetString());
        var privateHit = await Json(await _client.GetAsync("/api/search?q=Private%20Snow"));
        Assert.Equal(0, privateHit.GetProperty("total").GetInt32());
        Assert.Empty(privateHit.GetProperty("books").EnumerateArray());
    }

    [Fact]
    public async Task Rejects_A_Blank_Query_And_An_Invalid_Page_And_Caps_The_Limit()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/search?q=")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/search?q=%20")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/search?q=dune&limit=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/search?q=dune&limit=51")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/search?q=dune&offset=-1")).StatusCode);
    }

    [Fact]
    public async Task Search_Page_And_Home_Form_Are_Served()
    {
        var home = await _client.GetStringAsync("/");
        Assert.Contains("action=\"/search.html\"", home);
        Assert.Contains("Search title, author or ISBN", home);

        var page = await _client.GetAsync("/search.html");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("/find.js", html);
        Assert.Contains("/floatcard.js", html);
        var find = await _client.GetStringAsync("/find.js");
        Assert.DoesNotContain(".innerHTML", find);
        Assert.Contains("textContent", find);

        var account = await _client.GetStringAsync("/account.js");
        Assert.Contains("'/search.html'", account);
    }
}
