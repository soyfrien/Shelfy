using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Features.Shelves.SetShelfPublic;
using Shelfy.Infrastructure;

namespace Shelfy.Tests.Features.Shelves;

public class SetShelfPublicHandlerTests
{
    private static async Task<(ShelfyDbContext Db, Shelf Shelf, Guid OwnerId)> SeedAccountShelfAsync(bool isPublic = false, bool hidden = false)
    {
        var db = TestDb.Create();
        var ownerId = Guid.NewGuid();
        var user = new User { Id = ownerId, DisplayName = "Louis", City = "Brooklyn" };
        var account = new ShelfyAccount { Id = ownerId, UserName = "louis@example.com", Email = "louis@example.com", CreatedAt = DateTime.UtcNow };
        var shelf = new Shelf { Id = Guid.NewGuid(), UserId = ownerId, IsPublic = isPublic };
        if (hidden)
        {
            shelf.Hide(DateTime.UtcNow);
        }

        db.AddRange(user, account, shelf);
        await db.SaveChangesAsync();
        return (db, shelf, ownerId);
    }

    [Fact]
    public async Task Owner_Can_Turn_The_Shelf_On_And_Off()
    {
        var (db, shelf, ownerId) = await SeedAccountShelfAsync();
        await using (db)
        {
            var handler = new SetShelfPublicHandler(db);

            var on = await handler.HandleAsync(shelf.Id, new SetShelfPublicRequest(true), ownerId);
            Assert.True(on.IsSuccess);
            Assert.True(on.Value!.IsPublic);
            Assert.True((await db.Shelves.FindAsync(shelf.Id))!.IsPublic);

            var off = await handler.HandleAsync(shelf.Id, new SetShelfPublicRequest(false), ownerId);
            Assert.True(off.IsSuccess);
            Assert.False(off.Value!.IsPublic);
            Assert.False((await db.Shelves.FindAsync(shelf.Id))!.IsPublic);
        }
    }

    [Fact]
    public async Task Anonymous_Caller_Is_Refused()
    {
        var (db, shelf, _) = await SeedAccountShelfAsync();
        await using (db)
        {
            var result = await new SetShelfPublicHandler(db).HandleAsync(shelf.Id, new SetShelfPublicRequest(true), null);
            Assert.Equal(HandlerStatus.Unauthorized, result.Status);
            Assert.False((await db.Shelves.FindAsync(shelf.Id))!.IsPublic);
        }
    }

    [Fact]
    public async Task A_Different_Signed_In_User_Is_Refused()
    {
        var (db, shelf, _) = await SeedAccountShelfAsync();
        await using (db)
        {
            var result = await new SetShelfPublicHandler(db).HandleAsync(shelf.Id, new SetShelfPublicRequest(true), Guid.NewGuid());
            Assert.Equal(HandlerStatus.Forbidden, result.Status);
            Assert.False((await db.Shelves.FindAsync(shelf.Id))!.IsPublic);
        }
    }

    [Fact]
    public async Task A_Guest_Shelf_Cannot_Be_Toggled()
    {
        await using var db = TestDb.Create();
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Guest" };
        var shelf = new Shelf { Id = Guid.NewGuid(), UserId = user.Id };
        db.AddRange(user, shelf);
        await db.SaveChangesAsync();

        var result = await new SetShelfPublicHandler(db).HandleAsync(shelf.Id, new SetShelfPublicRequest(true), user.Id);
        Assert.Equal(HandlerStatus.Forbidden, result.Status);
        Assert.Contains("Guest", result.Error);
        Assert.False((await db.Shelves.FindAsync(shelf.Id))!.IsPublic);
    }

    [Fact]
    public async Task Unknown_Or_Hidden_Shelves_Are_Not_Found()
    {
        var (db, shelf, ownerId) = await SeedAccountShelfAsync(hidden: true);
        await using (db)
        {
            var handler = new SetShelfPublicHandler(db);
            Assert.Equal(HandlerStatus.NotFound, (await handler.HandleAsync(shelf.Id, new SetShelfPublicRequest(true), ownerId)).Status);
            Assert.Equal(HandlerStatus.NotFound, (await handler.HandleAsync(Guid.NewGuid(), new SetShelfPublicRequest(true), ownerId)).Status);
        }
    }
}
