using Shelfy.Domain;
using Shelfy.Features.Books.SearchBooks;
using Shelfy.Features.Common;

namespace Shelfy.Tests.Features.Books;

public class SearchBooksHandlerTests
{
    private static async Task<Shelfy.Infrastructure.ShelfyDbContext> SeededDbAsync()
    {
        var db = TestDb.Create();
        db.Books.AddRange(
            new Book { Id = Guid.NewGuid(), Title = "Snow Crash", Author = "Neal Stephenson", Isbn = "9780553380958" },
            new Book { Id = Guid.NewGuid(), Title = "Cryptonomicon", Author = "Neal Stephenson" },
            new Book { Id = Guid.NewGuid(), Title = "Dune", Author = "Frank Herbert" });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Finds_Books_By_Author_Case_Insensitively()
    {
        await using var db = await SeededDbAsync();

        var result = await new SearchBooksHandler(db).HandleAsync(new SearchBooksRequest("NEAL stephenson"));

        Assert.True(result.IsSuccess);
        Assert.Equal(["Cryptonomicon", "Snow Crash"], result.Value!.Books.Select(b => b.Title));
    }

    [Fact]
    public async Task Finds_A_Book_By_Isbn()
    {
        await using var db = await SeededDbAsync();

        var result = await new SearchBooksHandler(db).HandleAsync(new SearchBooksRequest("9780553380958"));

        Assert.Equal("Snow Crash", Assert.Single(result.Value!.Books).Title);
    }

    [Fact]
    public async Task Respects_Take()
    {
        await using var db = await SeededDbAsync();

        var result = await new SearchBooksHandler(db).HandleAsync(new SearchBooksRequest("a", Take: 1));

        Assert.Single(result.Value!.Books);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rejects_A_Blank_Query(string query)
    {
        await using var db = TestDb.Create();

        var result = await new SearchBooksHandler(db).HandleAsync(new SearchBooksRequest(query));

        Assert.Equal(HandlerStatus.Invalid, result.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(SearchBooksHandler.MaxTake + 1)]
    public async Task Rejects_An_Invalid_Take(int take)
    {
        await using var db = TestDb.Create();

        var result = await new SearchBooksHandler(db).HandleAsync(new SearchBooksRequest("dune", take));

        Assert.Equal(HandlerStatus.Invalid, result.Status);
    }
}
