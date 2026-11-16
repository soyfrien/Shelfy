using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Features.Lending;
using Shelfy.Features.Lending.Copies;
using Shelfy.Features.Lending.Labels;
using Shelfy.Infrastructure;
using Shelfy.Tests.Support;

namespace Shelfy.Tests.Features.Lending;

public class CopiesHandlerTests
{
    private sealed class ScriptedCodes(params string[] codes) : ICopyCodeGenerator
    {
        private int _i;
        public string Next() => codes[Math.Min(_i++, codes.Length - 1)];
    }

    private static async Task<(ShelfyDbContext Db, Guid Owner, Guid ShelfId, Guid BookId)> SeedAsync()
    {
        var db = TestDb.Create();
        var owner = new User { Id = Guid.NewGuid(), DisplayName = "Louis" };
        var shelf = new Shelf { Id = Guid.NewGuid(), UserId = owner.Id };
        var book = new Book { Id = Guid.NewGuid(), Title = "The Hobbit", Author = "J.R.R. Tolkien", Isbn = "9780547928227" };
        shelf.AddBook(book.Id);
        db.AddRange(owner, shelf, book);
        await db.SaveChangesAsync();
        return (db, owner.Id, shelf.Id, book.Id);
    }

    [Fact]
    public async Task Registering_a_copy_gives_it_a_code_a_history_entry_and_offers_the_book_for_loan()
    {
        var (db, owner, shelfId, bookId) = await SeedAsync();
        var handler = new CopiesHandler(db, new RandomCopyCodeGenerator(), new TestClock());

        var result = await handler.RegisterAsync(owner, shelfId, bookId, new RegisterCopyRequest("Good", null));

        Assert.True(result.IsSuccess, result.Error);
        var copy = result.Value!;
        Assert.NotNull(CopyCode.Normalize(copy.Code));
        Assert.Equal(CopyStatus.Available, copy.Status);
        Assert.False(copy.AllowPassAlong);
        Assert.Equal(CopyMovementKind.Registered, Assert.Single(copy.History).Kind);
        Assert.True(copy.Viewer.IsOwner);
        Assert.Equal(AvailabilityStatus.Loan, (await db.ShelfBooks.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_code_that_is_already_taken_is_skipped()
    {
        var (db, owner, shelfId, bookId) = await SeedAsync();
        var handler = new CopiesHandler(db, new ScriptedCodes("AAAAAAAA", "AAAAAAAA", "BBBBBBBB"), new TestClock());

        var first = await handler.RegisterAsync(owner, shelfId, bookId, new RegisterCopyRequest());
        var second = await handler.RegisterAsync(owner, shelfId, bookId, new RegisterCopyRequest());

        Assert.Equal("AAAAAAAA", first.Value!.Code);
        Assert.Equal("BBBBBBBB", second.Value!.Code);
    }

    [Fact]
    public async Task Gives_up_after_repeated_collisions()
    {
        var (db, owner, shelfId, bookId) = await SeedAsync();
        var handler = new CopiesHandler(db, new ScriptedCodes("AAAAAAAA"), new TestClock());
        await handler.RegisterAsync(owner, shelfId, bookId, new RegisterCopyRequest());

        var again = await handler.RegisterAsync(owner, shelfId, bookId, new RegisterCopyRequest());

        Assert.Equal(HandlerStatus.Conflict, again.Status);
    }

    [Fact]
    public async Task Only_the_shelf_owner_can_register_and_only_books_on_the_shelf()
    {
        var (db, owner, shelfId, bookId) = await SeedAsync();
        var handler = new CopiesHandler(db, new RandomCopyCodeGenerator(), new TestClock());

        Assert.Equal(HandlerStatus.Unauthorized, (await handler.RegisterAsync(null, shelfId, bookId, new())).Status);
        Assert.Equal(HandlerStatus.Forbidden, (await handler.RegisterAsync(Guid.NewGuid(), shelfId, bookId, new())).Status);
        Assert.Equal(HandlerStatus.NotFound, (await handler.RegisterAsync(owner, shelfId, Guid.NewGuid(), new())).Status);
        Assert.Equal(HandlerStatus.NotFound, (await handler.RegisterAsync(owner, Guid.NewGuid(), bookId, new())).Status);
    }

    [Fact]
    public async Task The_database_refuses_two_copies_with_the_same_code()
    {
        using var database = new SqliteTestDatabase();
        await using var db = database.CreateContext();
        var owner = new User { Id = Guid.NewGuid(), DisplayName = "Louis" };
        var book = new Book { Id = Guid.NewGuid(), Title = "Dune", Author = "Frank Herbert" };
        db.AddRange(owner, book);
        db.Copies.Add(Copy.Register(book.Id, owner.Id, null, "CCCCCCCC", DateTime.UtcNow));
        await db.SaveChangesAsync();

        db.Copies.Add(Copy.Register(book.Id, owner.Id, null, "CCCCCCCC", DateTime.UtcNow));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Concurrent_steps_on_one_copy_cannot_both_win()
    {
        using var database = new SqliteTestDatabase();
        Guid copyId;
        await using (var seed = database.CreateContext())
        {
            var owner = new User { Id = Guid.NewGuid(), DisplayName = "Louis" };
            var book = new Book { Id = Guid.NewGuid(), Title = "Dune", Author = "Frank Herbert" };
            var copy = Copy.Register(book.Id, owner.Id, null, "DDDDDDDD", DateTime.UtcNow);
            seed.AddRange(owner, book, copy);
            await seed.SaveChangesAsync();
            copyId = copy.Id;
        }

        await using var a = database.CreateContext();
        await using var b = database.CreateContext();
        var ca = await a.Copies.SingleAsync(c => c.Id == copyId);
        var cb = await b.Copies.SingleAsync(c => c.Id == copyId);
        ca.Status = CopyStatus.Reserved; ca.Version++;
        cb.Status = CopyStatus.Unavailable; cb.Version++;
        await a.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }

    [Fact]
    public void Label_is_an_svg_with_a_qr_code_and_the_formatted_code()
    {
        var svg = CopyLabel.Svg("https://shelfy.ppdac.ltd/c/7K3MQ9XA", "7K3MQ9XA", "The <Hobbit>");
        Assert.StartsWith("<svg", svg);
        Assert.Contains("7K3M-Q9XA", svg);
        Assert.Contains("The &lt;Hobbit&gt;", svg);
        Assert.Contains("<path", svg);
    }

    [Theory]
    [InlineData("A", 1)]
    [InlineData("https://shelfy.ppdac.ltd/c/7K3MQ9XA", 3)]
    public void Qr_picks_the_smallest_version_and_draws_finder_patterns(string text, int version)
    {
        var qr = QrCode.Encode(text);
        Assert.Equal(version, qr.Version);
        Assert.Equal(version * 4 + 17, qr.Size);
        // Finder pattern corners: dark outer ring, light separator ring, dark 3x3 centre.
        foreach (var (cx, cy) in new[] { (3, 3), (qr.Size - 4, 3), (3, qr.Size - 4) })
        {
            Assert.True(qr[cx - 3, cy - 3]);
            Assert.False(qr[cx - 2, cy - 2]);
            Assert.True(qr[cx, cy]);
        }

        Assert.True(qr[8, qr.Size - 8]);   // the always-dark module
    }

    [Fact]
    public void Qr_refuses_data_that_does_not_fit() =>
        Assert.Throws<ArgumentException>(() => QrCode.Encode(new string('x', 200)));
}
