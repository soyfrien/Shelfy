using Shelfy.Domain;
using Shelfy.Features.Books.SearchShelves;
using Shelfy.Features.Common;
using Shelfy.Infrastructure;

namespace Shelfy.Tests.Features.Books;

public class SearchShelvesHandlerTests
{
    private static readonly Guid PublicShelfId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PrivateShelfId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid HiddenShelfId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid GuestDarkShelfId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid DuneId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EncyclopediaId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SandNotesId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SecretId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static async Task<ShelfyDbContext> SeededDbAsync()
    {
        var db = TestDb.Create();
        var louis = new User { Id = Guid.NewGuid(), DisplayName = "  Louis  " };
        var wanjiru = new User { Id = Guid.NewGuid(), DisplayName = "Wanjiru" };
        var guest = new User { Id = Guid.NewGuid(), DisplayName = "" };
        var hiddenOwner = new User { Id = Guid.NewGuid(), DisplayName = "Hidden" };
        db.Users.AddRange(louis, wanjiru, guest, hiddenOwner);
        db.Accounts.Add(new ShelfyAccount
        {
            Id = guest.Id, UserName = "mystery-abc@example.com", Email = "mystery-abc@example.com",
            NormalizedUserName = "MYSTERY-ABC@EXAMPLE.COM", NormalizedEmail = "MYSTERY-ABC@EXAMPLE.COM"
        });

        var dune = new Book { Id = DuneId, Title = "Dune", Author = "Frank Herbert", Isbn = "978-0-441-17271-9" };
        var encyclopedia = new Book { Id = EncyclopediaId, Title = "The Dune Encyclopedia", Author = "Willis E. McNelly" };
        var sand = new Book { Id = SandNotesId, Title = "Sand Notes", Author = "Dune Fan" };
        var secret = new Book { Id = SecretId, Title = "Secret Book", Author = "Nobody", Isbn = "9780547928227" };
        var hiddenBook = new Book { Id = Guid.NewGuid(), Title = "Hidden Dune Diary", Author = "Anon" };
        var darkBook = new Book { Id = Guid.NewGuid(), Title = "Guest-only Dune Ledger", Author = "Anon" };
        db.Books.AddRange(dune, encyclopedia, sand, secret, hiddenBook, darkBook);

        var publicShelf = new Shelf { Id = PublicShelfId, UserId = louis.Id, IsPublic = true };
        publicShelf.AddBook(dune.Id);
        publicShelf.AddBook(encyclopedia.Id);
        publicShelf.AddBook(sand.Id);
        var otherPublic = new Shelf { Id = Guid.NewGuid(), UserId = wanjiru.Id, IsPublic = true };
        otherPublic.AddBook(dune.Id);
        var privateShelf = new Shelf { Id = PrivateShelfId, UserId = louis.Id, IsPublic = false };
        privateShelf.AddBook(secret.Id);
        privateShelf.AddBook(dune.Id);
        var hidden = new Shelf { Id = HiddenShelfId, UserId = hiddenOwner.Id, IsPublic = true };
        hidden.Hide(DateTime.UtcNow);
        hidden.AddBook(hiddenBook.Id);
        var guestDark = new Shelf { Id = GuestDarkShelfId, UserId = guest.Id, IsPublic = false };
        guestDark.AddBook(darkBook.Id);
        db.Shelves.AddRange(publicShelf, otherPublic, privateShelf, hidden, guestDark);

        db.Copies.Add(Copy.Register(dune.Id, louis.Id, publicShelf.Id, "DUNE0001", DateTime.UtcNow));
        await db.SaveChangesAsync();
        return db;
    }

    private static Task<HandlerResult<SearchShelvesResponse>> SearchAsync(ShelfyDbContext db, string q, int limit = 20, int offset = 0) =>
        new SearchShelvesHandler(db).HandleAsync(new SearchShelvesRequest(q, limit, offset));

    [Theory]
    [InlineData("9780441172719")]
    [InlineData("978-0-441-17271-9")]
    [InlineData("0441172717")]
    [InlineData("0-441-17271-7")]
    [InlineData(" 0441172717 ")]
    public async Task Finds_A_Book_By_Isbn10_Or_Isbn13_With_Or_Without_Hyphens(string query)
    {
        await using var db = await SeededDbAsync();

        var result = await SearchAsync(db, query);

        Assert.True(result.IsSuccess);
        var book = Assert.Single(result.Value!.Books);
        Assert.Equal("Dune", book.Title);
        Assert.Equal("9780441172719", book.Isbn);
        Assert.True(book.Available);
        Assert.Equal(2, book.Shelves.Count);
        Assert.Contains(book.Shelves, s => s.OwnerName == "Louis" && s.Available && s.Copies.Any(c => c.Code == "DUNE0001"));
        Assert.Contains(book.Shelves, s => s.OwnerName == "Wanjiru" && !s.Available);
        Assert.DoesNotContain(book.Shelves, s => s.Id == PrivateShelfId);
        Assert.All(book.Shelves, s => Assert.DoesNotContain('@', s.OwnerName));
    }

    [Fact]
    public async Task Ranks_Exact_Isbn_First_Then_Title_Then_Author()
    {
        await using var db = await SeededDbAsync();

        var byName = await SearchAsync(db, "dune");
        Assert.Equal(["Dune", "The Dune Encyclopedia", "Sand Notes"], byName.Value!.Books.Select(b => b.Title));

        var byIsbn = await SearchAsync(db, "044-117-271-7");
        Assert.Equal("Dune", byIsbn.Value!.Books[0].Title);
        Assert.Equal(0, byIsbn.Value.Offset);
    }

    [Fact]
    public async Task Excludes_Private_Hidden_And_Guest_Dark_Shelves()
    {
        await using var db = await SeededDbAsync();

        var secret = await SearchAsync(db, "Secret Book");
        Assert.Empty(secret.Value!.Books);
        Assert.Equal(0, secret.Value.Total);

        var hidden = await SearchAsync(db, "Hidden Dune Diary");
        Assert.Empty(hidden.Value!.Books);

        var dark = await SearchAsync(db, "Guest-only Dune Ledger");
        Assert.Empty(dark.Value!.Books);

        var dune = await SearchAsync(db, "Dune");
        Assert.DoesNotContain(dune.Value!.Books.SelectMany(b => b.Shelves), s => s.Id == PrivateShelfId || s.Id == HiddenShelfId || s.Id == GuestDarkShelfId);
        Assert.DoesNotContain(dune.Value.Books, b => b.Title.Contains("Secret") || b.Title.Contains("Hidden") || b.Title.Contains("Ledger"));
    }

    [Fact]
    public async Task Never_Returns_An_Email_Even_For_A_Public_Guest_Shelf()
    {
        await using var db = await SeededDbAsync();
        var shelf = await db.Shelves.FindAsync(GuestDarkShelfId);
        shelf!.IsPublic = true;
        await db.SaveChangesAsync();

        var result = await SearchAsync(db, "Guest-only Dune Ledger");
        var book = Assert.Single(result.Value!.Books);
        var hold = Assert.Single(book.Shelves);
        Assert.Equal("mystery-abc", hold.OwnerName);
        Assert.DoesNotContain("@", hold.OwnerName);
        Assert.DoesNotContain("example.com", hold.OwnerName);
    }

    [Fact]
    public async Task Paginates_And_Rejects_Bad_Input()
    {
        await using var db = await SeededDbAsync();

        var page = await SearchAsync(db, "dune", limit: 1, offset: 1);
        Assert.Equal(3, page.Value!.Total);
        Assert.Equal(1, page.Value.Limit);
        Assert.Equal(1, page.Value.Offset);
        Assert.Equal("The Dune Encyclopedia", Assert.Single(page.Value.Books).Title);

        await using var empty = TestDb.Create();
        Assert.Equal(HandlerStatus.Invalid, (await SearchAsync(empty, "  ")).Status);
        Assert.Equal(HandlerStatus.Invalid, (await SearchAsync(empty, "dune", limit: 0)).Status);
        Assert.Equal(HandlerStatus.Invalid, (await SearchAsync(empty, "dune", limit: SearchShelvesHandler.MaxLimit + 1)).Status);
        Assert.Equal(HandlerStatus.Invalid, (await SearchAsync(empty, "dune", offset: -1)).Status);
        Assert.Equal(HandlerStatus.Invalid, (await new SearchShelvesHandler(empty).HandleAsync(new SearchShelvesRequest(new string('a', SearchShelvesHandler.MaxQueryLength + 1)))).Status);
    }
}
