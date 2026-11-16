using Shelfy.Domain;

namespace Shelfy.Tests.Domain;

public class ShelfTests
{
    [Fact]
    public void New_Shelf_Is_Not_Public()
    {
        Assert.False(new Shelf().IsPublic);
    }

    [Fact]
    public void SetPublic_Turns_Sharing_On_And_Off()
    {
        var shelf = new Shelf();
        shelf.SetPublic(true);
        Assert.True(shelf.IsPublic);
        shelf.SetPublic(false);
        Assert.False(shelf.IsPublic);
    }

    [Fact]
    public void AddBook_Adds_A_Private_ShelfBook()
    {
        var shelf = new Shelf { Id = Guid.NewGuid() };
        var bookId = Guid.NewGuid();

        var added = shelf.AddBook(bookId);

        Assert.NotNull(added);
        Assert.Equal(shelf.Id, added.ShelfId);
        Assert.Equal(AvailabilityStatus.Private, added.Status);
        Assert.Single(shelf.Books);
    }

    [Fact]
    public void AddBook_Does_Not_Add_The_Same_Book_Twice()
    {
        var shelf = new Shelf { Id = Guid.NewGuid() };
        var bookId = Guid.NewGuid();
        shelf.AddBook(bookId);

        var second = shelf.AddBook(bookId);

        Assert.Null(second);
        Assert.Single(shelf.Books);
    }
}
