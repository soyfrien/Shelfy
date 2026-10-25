namespace Shelfy.Domain;

/// <summary>A user's collection of books.</summary>
public class Shelf
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Public sharing is off by default (docs/requirements/public-shelf-page.md).</summary>
    public bool IsPublic { get; set; }

    public ICollection<ShelfBook> Books { get; set; } = new List<ShelfBook>();

    /// <summary>Adds a book as Private. A book can only be on a shelf once.</summary>
    /// <returns>The new ShelfBook, or null when the book is already on the shelf.</returns>
    public ShelfBook? AddBook(Guid bookId)
    {
        if (Books.Any(b => b.BookId == bookId))
        {
            return null;
        }

        var shelfBook = new ShelfBook { ShelfId = Id, BookId = bookId };
        Books.Add(shelfBook);
        return shelfBook;
    }
}
