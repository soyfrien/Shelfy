using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Features.Shelves.CreateShelf;

namespace Shelfy.Tests.Features.Shelves;

public class CreateShelfHandlerTests
{
    [Fact]
    public async Task Creates_A_Private_Shelf_For_An_Existing_User()
    {
        await using var db = TestDb.Create();
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Louis", City = "Brooklyn" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await new CreateShelfHandler(db).HandleAsync(new CreateShelfRequest(user.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, result.Value!.UserId);
        Assert.False(result.Value.IsPublic);
        Assert.Single(db.Shelves);
    }

    [Fact]
    public async Task Returns_NotFound_For_An_Unknown_User()
    {
        await using var db = TestDb.Create();

        var result = await new CreateShelfHandler(db).HandleAsync(new CreateShelfRequest(Guid.NewGuid()));

        Assert.Equal(HandlerStatus.NotFound, result.Status);
        Assert.Empty(db.Shelves);
    }

    [Fact]
    public async Task Returns_Invalid_For_An_Empty_UserId()
    {
        await using var db = TestDb.Create();

        var result = await new CreateShelfHandler(db).HandleAsync(new CreateShelfRequest(Guid.Empty));

        Assert.Equal(HandlerStatus.Invalid, result.Status);
    }
}
