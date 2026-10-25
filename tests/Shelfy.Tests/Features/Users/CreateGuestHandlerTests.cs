using Shelfy.Features.Users.CreateGuest;

namespace Shelfy.Tests.Features.Users;

public class CreateGuestHandlerTests
{
    [Fact]
    public async Task Creates_An_Anonymous_Guest_User()
    {
        await using var db = TestDb.Create();

        var result = await new CreateGuestHandler(db).HandleAsync();

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value!.Id);
        var user = Assert.Single(db.Users);
        Assert.Equal(result.Value.Id, user.Id);
        Assert.Null(user.IdentitySubject);
    }
}
