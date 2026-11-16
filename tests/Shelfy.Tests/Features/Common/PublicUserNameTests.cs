using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Infrastructure;

namespace Shelfy.Tests.Features.Common;

public class PublicUserNameTests
{
    [Fact]
    public void Prefers_the_display_name()
    {
        var user = new User { DisplayName = "  Louis  " };
        var account = new ShelfyAccount { UserName = "louis@example.com", Email = "louis@example.com" };
        Assert.Equal("Louis", PublicUserName.Of(user, account));
    }

    [Fact]
    public void Falls_back_to_a_non_email_username()
    {
        var account = new ShelfyAccount { UserName = "bookworm", Email = "hidden@example.com" };
        Assert.Equal("bookworm", PublicUserName.Of(new User { DisplayName = " " }, account));
    }

    [Fact]
    public void Falls_back_to_the_email_local_part_and_never_returns_the_full_email()
    {
        var account = new ShelfyAccount { UserName = "mystery-abc@example.com", Email = "mystery-abc@example.com" };
        var name = PublicUserName.Of(new User { DisplayName = "" }, account);
        Assert.Equal("mystery-abc", name);
        Assert.DoesNotContain("@", name);
        Assert.DoesNotContain("example.com", name);
    }

    [Fact]
    public void Uses_someone_when_nothing_is_known()
    {
        Assert.Equal("Someone", PublicUserName.Of(null, null));
        Assert.Equal("Someone", PublicUserName.Of(new User(), new ShelfyAccount()));
    }
}
