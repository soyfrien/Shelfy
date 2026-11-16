using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Domain;
using Shelfy.Infrastructure;

namespace Shelfy.Tests.Api;

/// <summary>PATCH /api/shelves/{id}: the signed-in owner of an account shelf can show it in cross-shelf search.</summary>
public class SetShelfPublicEndpointTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly List<HttpClient> _clients = new();

    public void Dispose()
    {
        _clients.ForEach(c => c.Dispose());
        _factory.Dispose();
    }

    private HttpClient NewClient()
    {
        var c = _factory.CreateClient(new() { AllowAutoRedirect = false });
        _clients.Add(c);
        return c;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(string.IsNullOrEmpty(text) ? "null" : text).RootElement.Clone();
    }

    private async Task<(HttpClient Client, Guid Id)> SignUpAsync(string name)
    {
        var c = NewClient();
        var email = $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";
        var r = await c.PostAsJsonAsync("/api/account/register",
            new { email, password = "correct horse", displayName = name, mailingAddress = "1 Main St\nBrooklyn NY" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var link = await NewClient().GetAsync(_factory.Mailer.ConfirmLink(email));
        Assert.Equal("/signin.html?confirmed=1", link.Headers.Location?.OriginalString);
        return (c, (await Json(r)).GetProperty("id").GetGuid());
    }

    private async Task<(Guid ShelfId, Guid BookId)> ShelfWithBookAsync(HttpClient owner, Guid ownerId, string title = "The Left Hand of Darkness")
    {
        var r = await owner.PostAsJsonAsync("/api/shelves", new { userId = ownerId });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var shelf = await Json(r);
        var shelfId = shelf.GetProperty("id").GetGuid();
        Assert.False(shelf.GetProperty("isPublic").GetBoolean());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
        var book = new Book { Id = Guid.NewGuid(), Title = title, Author = "Ursula K. Le Guin", Isbn = "9780441478125" };
        db.Books.Add(book);
        db.ShelfBooks.Add(new ShelfBook { ShelfId = shelfId, BookId = book.Id });
        await db.SaveChangesAsync();
        return (shelfId, book.Id);
    }

    [Fact]
    public async Task Owner_Can_Toggle_IsPublic_And_The_Shelf_Then_Appears_In_Search()
    {
        var (owner, ownerId) = await SignUpAsync("Louis");
        var (shelfId, _) = await ShelfWithBookAsync(owner, ownerId);
        var anon = NewClient();

        var before = await Json(await anon.GetAsync($"/api/shelves/{shelfId}"));
        Assert.False(before.GetProperty("isPublic").GetBoolean());
        var missed = await Json(await anon.GetAsync("/api/search?q=Left%20Hand%20of%20Darkness"));
        Assert.Equal(0, missed.GetProperty("total").GetInt32());

        var on = await owner.PatchAsJsonAsync($"/api/shelves/{shelfId}", new { isPublic = true });
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        var toggled = await Json(on);
        Assert.Equal(shelfId, toggled.GetProperty("id").GetGuid());
        Assert.True(toggled.GetProperty("isPublic").GetBoolean());

        var after = await Json(await anon.GetAsync($"/api/shelves/{shelfId}"));
        Assert.True(after.GetProperty("isPublic").GetBoolean());

        var hit = await Json(await anon.GetAsync("/api/search?q=Left%20Hand%20of%20Darkness"));
        Assert.Equal(1, hit.GetProperty("total").GetInt32());
        var book = Assert.Single(hit.GetProperty("books").EnumerateArray());
        Assert.Equal("The Left Hand of Darkness", book.GetProperty("title").GetString());
        Assert.Equal(shelfId, Assert.Single(book.GetProperty("shelves").EnumerateArray()).GetProperty("id").GetGuid());

        var off = await owner.PatchAsJsonAsync($"/api/shelves/{shelfId}", new { isPublic = false });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.False((await Json(off)).GetProperty("isPublic").GetBoolean());
        var gone = await Json(await anon.GetAsync("/api/search?q=Left%20Hand%20of%20Darkness"));
        Assert.Equal(0, gone.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task A_Different_Signed_In_User_Is_Forbidden()
    {
        var (owner, ownerId) = await SignUpAsync("Louis");
        var (other, _) = await SignUpAsync("Mallory");
        var (shelfId, _) = await ShelfWithBookAsync(owner, ownerId);

        var r = await other.PatchAsJsonAsync($"/api/shelves/{shelfId}", new { isPublic = true });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.False((await Json(await NewClient().GetAsync($"/api/shelves/{shelfId}"))).GetProperty("isPublic").GetBoolean());
    }

    [Fact]
    public async Task An_Anonymous_Caller_Is_Unauthorized()
    {
        var (owner, ownerId) = await SignUpAsync("Louis");
        var (shelfId, _) = await ShelfWithBookAsync(owner, ownerId);

        var r = await NewClient().PatchAsJsonAsync($"/api/shelves/{shelfId}", new { isPublic = true });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.False((await Json(await NewClient().GetAsync($"/api/shelves/{shelfId}"))).GetProperty("isPublic").GetBoolean());
    }

    [Fact]
    public async Task A_Guest_Shelf_Cannot_Be_Toggled()
    {
        var guestId = await _factory.CreateUserAsync();
        var created = await NewClient().PostAsJsonAsync("/api/shelves", new { userId = guestId });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var shelfId = (await Json(created)).GetProperty("id").GetGuid();

        var (signedIn, _) = await SignUpAsync("Louis");
        Assert.Equal(HttpStatusCode.Unauthorized, (await NewClient().PatchAsJsonAsync($"/api/shelves/{shelfId}", new { isPublic = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await signedIn.PatchAsJsonAsync($"/api/shelves/{shelfId}", new { isPublic = true })).StatusCode);
        Assert.False((await Json(await NewClient().GetAsync($"/api/shelves/{shelfId}"))).GetProperty("isPublic").GetBoolean());
    }
}
