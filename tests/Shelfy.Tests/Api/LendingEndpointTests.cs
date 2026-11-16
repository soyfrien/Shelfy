using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shelfy.Domain;
using Shelfy.Infrastructure;
using Shelfy.Tests.Support;

namespace Shelfy.Tests.Api;

/// <summary>Accounts and lending through the real host (ADR-012): cookies, authorization, address privacy, the label page.</summary>
public class LendingEndpointTests : IDisposable
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

    private async Task<(HttpClient Client, Guid Id)> SignUpAsync(string name, string? address = "1 Main St\nBrooklyn NY")
    {
        var c = NewClient();
        var r = await c.PostAsJsonAsync("/api/account/register",
            new { email = $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com", password = "correct horse", displayName = name, mailingAddress = address });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (c, (await Json(r)).GetProperty("id").GetGuid());
    }

    /// <summary>A shelf owned by the signed-in client with one book on it.</summary>
    private async Task<(Guid ShelfId, Guid BookId)> ShelfWithBookAsync(HttpClient owner, Guid ownerId)
    {
        var r = await owner.PostAsJsonAsync("/api/shelves", new { userId = ownerId });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var shelfId = (await Json(r)).GetProperty("id").GetGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfyDbContext>();
        var book = new Book { Id = Guid.NewGuid(), Title = "The Hobbit", Author = "J.R.R. Tolkien", Isbn = Guid.NewGuid().ToString("N")[..13] };
        db.Books.Add(book);
        db.ShelfBooks.Add(new ShelfBook { ShelfId = shelfId, BookId = book.Id });
        await db.SaveChangesAsync();
        return (shelfId, book.Id);
    }

    private async Task<(Guid CopyId, string Code)> LendableCopyAsync(HttpClient owner, Guid ownerId)
    {
        var (shelfId, bookId) = await ShelfWithBookAsync(owner, ownerId);
        var r = await owner.PostAsJsonAsync($"/api/shelves/{shelfId}/books/{bookId}/copies", new { });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var j = await Json(r);
        return (j.GetProperty("id").GetGuid(), j.GetProperty("code").GetString()!);
    }

    private static async Task<JsonElement> StepAsync(HttpClient c, Guid loanId, string action, object? body = null, HttpStatusCode expect = HttpStatusCode.OK)
    {
        var r = await c.PostAsJsonAsync($"/api/loans/{loanId}/{action}", body ?? new { });
        Assert.True(expect == r.StatusCode, $"{action}: expected {expect}, got {r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return await Json(r);
    }

    [Fact]
    public async Task Register_sign_out_and_sign_in_with_a_cookie()
    {
        var c = NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/account/me")).StatusCode);

        var email = $"louis-{Guid.NewGuid():N}@example.com";
        var reg = await c.PostAsJsonAsync("/api/account/register", new { email, password = "correct horse", displayName = "Louis", mailingAddress = "1 Main St" });
        Assert.Equal(HttpStatusCode.Created, reg.StatusCode);
        Assert.Contains(reg.Headers.GetValues("Set-Cookie"), v => v.StartsWith("shelfy.auth=") && v.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        var me = await Json(await c.GetAsync("/api/account/me"));
        Assert.Equal("Louis", me.GetProperty("displayName").GetString());
        Assert.Equal("1 Main St", me.GetProperty("mailingAddress").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await NewClient().PostAsJsonAsync("/api/account/register", new { email, password = "another pass", displayName = "X" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/account/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/account/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/account/login", new { email, password = "wrong password" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/account/login", new { email, password = "correct horse" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/account/me")).StatusCode);
    }

    [Fact]
    public async Task Registration_validates_input()
    {
        var c = NewClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/account/register", new { email = "nope", password = "correct horse", displayName = "A" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/account/register", new { email = "a@b.c", password = "short", displayName = "A" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/account/register", new { email = "a@b.c", password = "correct horse", displayName = " " })).StatusCode);
    }

    [Fact]
    public async Task Lending_endpoints_need_sign_in()
    {
        var anon = NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/loans")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync($"/api/copies/{Guid.NewGuid()}/borrow", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync($"/api/loans/{Guid.NewGuid()}/approve", new { })).StatusCode);
    }

    [Fact]
    public async Task Full_loan_over_http_with_receive_by_code_and_address_privacy()
    {
        var (owner, ownerId) = await SignUpAsync("Louis", "1 Owner Way");
        var (alice, aliceId) = await SignUpAsync("Alice", "2 Borrower Rd");
        var (stranger, _) = await SignUpAsync("Mallory");
        var (copyId, code) = await LendableCopyAsync(owner, ownerId);
        var (aliceShelf, _) = await ShelfWithBookAsync(alice, aliceId);

        // Public copy views carry no addresses.
        var publicCopy = await (await NewClient().GetAsync($"/api/copies/by-code/{code.ToLowerInvariant()}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Owner Way", publicCopy);

        var ask = await alice.PostAsJsonAsync($"/api/copies/{copyId}/borrow", new { message = "Can't wait!" });
        Assert.Equal(HttpStatusCode.Created, ask.StatusCode);
        var loanId = (await Json(ask)).GetProperty("id").GetGuid();

        // Before approval nobody sees an address; strangers can't see the loan at all; the borrower can't approve.
        Assert.Equal(JsonValueKind.Null, (await Json(await owner.GetAsync($"/api/loans/{loanId}"))).GetProperty("shipTo").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/loans/{loanId}")).StatusCode);
        await StepAsync(alice, loanId, "approve", expect: HttpStatusCode.Forbidden);
        await StepAsync(stranger, loanId, "approve", expect: HttpStatusCode.NotFound);

        var approved = await StepAsync(owner, loanId, "approve");
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.Equal("2 Borrower Rd", approved.GetProperty("shipTo").GetString());              // owner sees where to mail it
        var aliceView = await Json(await alice.GetAsync($"/api/loans/{loanId}"));
        Assert.Equal("1 Owner Way", aliceView.GetProperty("shipTo").GetString());              // borrower sees the return address
        Assert.DoesNotContain("Owner Way", await (await stranger.GetAsync($"/api/copies/{copyId}")).Content.ReadAsStringAsync());

        await StepAsync(alice, loanId, "mailed", expect: HttpStatusCode.Forbidden);
        Assert.Equal("Mailed", (await StepAsync(owner, loanId, "mailed")).GetProperty("status").GetString());

        // The sticker: the stranger can't receive it, Alice can (by code, in any spelling).
        Assert.Equal(HttpStatusCode.Conflict, (await stranger.PostAsJsonAsync($"/api/copies/by-code/{code}/received", new { })).StatusCode);
        var dashed = code[..4] + "-" + code[4..].ToLowerInvariant();
        var received = await alice.PostAsJsonAsync($"/api/copies/by-code/{dashed}/received", new { shelfId = aliceShelf });
        Assert.Equal(HttpStatusCode.OK, received.StatusCode);
        Assert.Equal("Received", (await Json(received)).GetProperty("status").GetString());

        var copy = await Json(await alice.GetAsync($"/api/copies/{copyId}"));
        Assert.Equal("OnLoan", copy.GetProperty("status").GetString());
        Assert.Equal("Alice", copy.GetProperty("holderName").GetString());
        Assert.Equal("Louis", copy.GetProperty("ownerName").GetString());
        Assert.True(copy.GetProperty("viewer").GetProperty("isHolder").GetBoolean());
        var history = copy.GetProperty("history");
        Assert.Equal(2, history.GetArrayLength());
        Assert.Equal("Lent", history[1].GetProperty("kind").GetString());

        // It now shows on Alice's shelf as a visiting copy.
        var onAliceShelf = await Json(await NewClient().GetAsync($"/api/shelves/{aliceShelf}/copies"));
        Assert.True(onAliceShelf.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == copyId).GetProperty("isVisiting").GetBoolean());

        await StepAsync(owner, loanId, "request-return");
        await StepAsync(alice, loanId, "return-mailed");
        var home = await owner.PostAsJsonAsync($"/api/copies/by-code/{code}/received", new { });   // owner scans it when it comes home
        Assert.Equal("Returned", (await Json(home)).GetProperty("status").GetString());

        // Finished: addresses are no longer shared.
        Assert.Equal(JsonValueKind.Null, (await Json(await alice.GetAsync($"/api/loans/{loanId}"))).GetProperty("shipTo").ValueKind);
        var mine = await Json(await alice.GetAsync("/api/loans?all=true"));
        Assert.Equal(loanId, mine.GetProperty("borrowing")[0].GetProperty("id").GetGuid());
        var ownerList = await Json(await owner.GetAsync("/api/loans"));
        Assert.Equal(0, ownerList.GetProperty("lending").GetArrayLength());   // closed loans hidden by default
    }

    [Fact]
    public async Task Borrowing_needs_a_mailing_address_and_reserving_joins_the_waitlist()
    {
        var (owner, ownerId) = await SignUpAsync("Louis");
        var (noAddress, _) = await SignUpAsync("Nomad", address: null);
        var (alice, _) = await SignUpAsync("Alice");
        var (bob, _) = await SignUpAsync("Bob");
        var (copyId, _) = await LendableCopyAsync(owner, ownerId);

        Assert.Equal(HttpStatusCode.BadRequest, (await noAddress.PostAsJsonAsync($"/api/copies/{copyId}/borrow", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync($"/api/copies/{copyId}/borrow", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await alice.PostAsJsonAsync($"/api/copies/{copyId}/borrow", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await bob.PostAsJsonAsync($"/api/copies/{copyId}/borrow", new { })).StatusCode);
        var res = await bob.PostAsJsonAsync($"/api/copies/{copyId}/reserve", new { });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var j = await Json(res);
        Assert.Equal("Waitlisted", j.GetProperty("status").GetString());
        Assert.Equal(1, j.GetProperty("waitlistPosition").GetInt32());

        var forShelf = await Json(await bob.GetAsync($"/api/copies/{copyId}"));
        Assert.Equal(1, forShelf.GetProperty("waiting").GetInt32());
        Assert.Equal("Waitlisted", forShelf.GetProperty("viewer").GetProperty("myLoan").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Copy_settings_are_owner_only()
    {
        var (owner, ownerId) = await SignUpAsync("Louis");
        var (alice, _) = await SignUpAsync("Alice");
        var (copyId, _) = await LendableCopyAsync(owner, ownerId);

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PutAsJsonAsync($"/api/copies/{copyId}", new { allowPassAlong = true })).StatusCode);
        var r = await owner.PutAsJsonAsync($"/api/copies/{copyId}", new { allowPassAlong = true, available = false });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await Json(r);
        Assert.True(j.GetProperty("allowPassAlong").GetBoolean());
        Assert.Equal("Unavailable", j.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Pass_along_over_http_when_the_owner_allows_it()
    {
        var (owner, ownerId) = await SignUpAsync("Louis");
        var (alice, _) = await SignUpAsync("Alice", "2 Alice Rd");
        var (bob, _) = await SignUpAsync("Bob", "3 Bob Ave");
        var (copyId, code) = await LendableCopyAsync(owner, ownerId);
        await owner.PutAsJsonAsync($"/api/copies/{copyId}", new { allowPassAlong = true });

        var first = (await Json(await alice.PostAsJsonAsync($"/api/copies/{copyId}/borrow", new { }))).GetProperty("id").GetGuid();
        await StepAsync(owner, first, "approve");
        await StepAsync(owner, first, "mailed");
        await StepAsync(alice, first, "received");
        var next = (await Json(await bob.PostAsJsonAsync($"/api/copies/{copyId}/reserve", new { }))).GetProperty("id").GetGuid();

        await StepAsync(alice, first, "pass-along", expect: HttpStatusCode.Forbidden);
        var afterPass = await StepAsync(owner, first, "pass-along");
        Assert.Equal("Received", afterPass.GetProperty("status").GetString());

        // Alice is now the sender of Bob's loan and sees Bob's address (and only while she has to mail it).
        var aliceSees = await Json(await alice.GetAsync($"/api/loans/{next}"));
        Assert.Equal("sender", aliceSees.GetProperty("role").GetString());
        Assert.Equal("3 Bob Ave", aliceSees.GetProperty("shipTo").GetString());
        Assert.Contains("mailed", aliceSees.GetProperty("actions").EnumerateArray().Select(a => a.GetString()));

        await StepAsync(alice, next, "mailed");
        Assert.Equal("PassedOn", (await Json(await alice.GetAsync($"/api/loans/{first}"))).GetProperty("status").GetString());
        await bob.PostAsJsonAsync($"/api/copies/by-code/{code}/received", new { });
        var copy = await Json(await bob.GetAsync($"/api/copies/{copyId}"));
        Assert.Equal("Bob", copy.GetProperty("holderName").GetString());
        Assert.Equal("Louis", copy.GetProperty("ownerName").GetString());
        Assert.Equal("PassedAlong", copy.GetProperty("history")[2].GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, (await Json(await alice.GetAsync($"/api/loans/{next}"))).GetProperty("shipTo").ValueKind);
    }

    [Fact]
    public async Task Account_shelves_can_only_be_changed_by_their_owner_while_guest_shelves_stay_open()
    {
        var (owner, ownerId) = await SignUpAsync("Louis");
        var (other, _) = await SignUpAsync("Mallory");
        var (shelfId, _) = await ShelfWithBookAsync(owner, ownerId);
        var anon = NewClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync($"/api/shelves/{shelfId}/hide", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.DeleteAsync($"/api/shelves/{shelfId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync("/api/shelves", new { userId = ownerId })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anon.PostAsJsonAsync("/api/shelves", new { userId = ownerId })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/api/shelves/{shelfId}")).StatusCode);   // reads stay public
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync($"/api/shelves/{shelfId}/books/{Guid.NewGuid()}/copies", new { })).StatusCode);

        var guestId = await _factory.CreateUserAsync();
        var guestShelf = await anon.PostAsJsonAsync("/api/shelves", new { userId = guestId });
        Assert.Equal(HttpStatusCode.Created, guestShelf.StatusCode);
        var guestShelfId = (await Json(guestShelf)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await anon.PostAsync($"/api/shelves/{guestShelfId}/hide", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/shelves/{shelfId}/hide", null)).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_shelf_keeps_its_copies()
    {
        var (owner, ownerId) = await SignUpAsync("Louis");
        var (shelfId, bookId) = await ShelfWithBookAsync(owner, ownerId);
        var reg = await owner.PostAsJsonAsync($"/api/shelves/{shelfId}/books/{bookId}/copies", new { });
        var copyId = (await Json(reg)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/shelves/{shelfId}")).StatusCode);

        var copy = await Json(await owner.GetAsync($"/api/copies/{copyId}"));
        Assert.Equal(JsonValueKind.Null, copy.GetProperty("homeShelfId").ValueKind);
    }

    [Fact]
    public async Task Copy_page_and_label()
    {
        var (owner, ownerId) = await SignUpAsync("Louis");
        var (copyId, code) = await LendableCopyAsync(owner, ownerId);
        var anon = NewClient();

        var page = await anon.GetAsync($"/c/{code}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("a Shelfy copy", html);
        Assert.Contains("The Hobbit", html);
        Assert.Contains("/copy.js", html);

        var lower = await anon.GetAsync($"/c/{code[..4].ToLowerInvariant()}-{code[4..]}");
        Assert.Equal(HttpStatusCode.Redirect, lower.StatusCode);
        Assert.Equal($"/c/{code}", lower.Headers.Location!.ToString());

        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/c/ZZZZZZZZ")).StatusCode);

        var label = await anon.GetAsync($"/api/copies/{copyId}/label.svg");
        Assert.Equal("image/svg+xml", label.Content.Headers.ContentType!.MediaType);
        var svg = await label.Content.ReadAsStringAsync();
        Assert.Contains(code[..4] + "-" + code[4..], svg);
    }

    [Fact]
    public async Task Web_pages_for_accounts_are_served()
    {
        var anon = NewClient();
        foreach (var path in new[] { "/signin.html", "/loans.html", "/account.js", "/copy.js", "/loans.js", "/signin.js" })
        {
            Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(path)).StatusCode);
        }
    }
}
