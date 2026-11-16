namespace Shelfy.Domain;

/// <summary>A user's collection of books.</summary>
public class Shelf
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Public sharing is off by default (docs/requirements/public-shelf-page.md).</summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// Soft-cleared by its owner ("Hide shelf", ADR-010): the shelf and its data are kept but it is no longer shown, shared or
    /// accepting photos. Reversible by an operator (clear the flag), never by the public API.
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>When the shelf was hidden (UTC); null while visible.</summary>
    public DateTime? HiddenAt { get; set; }

    /// <summary>
    /// Shows or hides the shelf in cross-shelf search. A private shelf can still be opened by its link.
    /// </summary>
    public void SetPublic(bool isPublic) => IsPublic = isPublic;

    /// <summary>Hides the shelf. Calling it again keeps the original hidden time.</summary>
    public void Hide(DateTime nowUtc)
    {
        if (IsHidden)
        {
            return;
        }

        IsHidden = true;
        HiddenAt = nowUtc;
    }

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
