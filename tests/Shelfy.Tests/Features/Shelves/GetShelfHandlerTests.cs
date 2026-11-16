using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Features.Shelves.GetShelf;

namespace Shelfy.Tests.Features.Shelves;

public class GetShelfHandlerTests
{
    [Fact]
    public async Task Returns_The_Shelf_With_Its_Books_Ordered_By_Title()
    {
        await using var db = TestDb.Create();
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Louis", City = "Brooklyn" };
        var dune = new Book { Id = Guid.NewGuid(), Title = "Dune", Author = "Frank Herbert" };
        var foundation = new Book { Id = Guid.NewGuid(), Title = "Foundation", Author = "Isaac Asimov" };
        var shelf = new Shelf { Id = Guid.NewGuid(), UserId = user.Id };
        shelf.AddBook(foundation.Id);
        shelf.AddBook(dune.Id);
        db.AddRange(user, dune, foundation, shelf);
        await db.SaveChangesAsync();

        var result = await new GetShelfHandler(db).HandleAsync(new GetShelfRequest(shelf.Id));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsPublic);
        Assert.Equal("Louis", result.Value.OwnerName);
        Assert.Equal(["Dune", "Foundation"], result.Value.Books.Select(b => b.Title));
        Assert.All(result.Value.Books, b => Assert.Equal(AvailabilityStatus.Private, b.Status));
    }

    [Fact]
    public async Task Returns_NotFound_For_An_Unknown_Shelf()
    {
        await using var db = TestDb.Create();

        var result = await new GetShelfHandler(db).HandleAsync(new GetShelfRequest(Guid.NewGuid()));

        Assert.Equal(HandlerStatus.NotFound, result.Status);
    }
}
