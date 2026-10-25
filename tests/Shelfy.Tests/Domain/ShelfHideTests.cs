using Shelfy.Domain;

namespace Shelfy.Tests.Domain;

public class ShelfHideTests
{
    [Fact]
    public void A_New_Shelf_Is_Visible()
    {
        var shelf = new Shelf { Id = Guid.NewGuid() };

        Assert.False(shelf.IsHidden);
        Assert.Null(shelf.HiddenAt);
    }

    [Fact]
    public void Hide_Marks_The_Shelf_And_Is_Idempotent()
    {
        var shelf = new Shelf { Id = Guid.NewGuid() };
        var first = new DateTime(2026, 10, 25, 20, 0, 0, DateTimeKind.Utc);

        shelf.Hide(first);
        shelf.Hide(first.AddHours(1));

        Assert.True(shelf.IsHidden);
        Assert.Equal(first, shelf.HiddenAt);
    }
}
