# Domain

The terms below are **frozen**. Do not rename them or introduce synonyms. Changing them requires a new ADR.

## Glossary

| Term | Meaning |
|---|---|
| User | A person with a Shelfy account. |
| Shelf | A user's collection of books, built from photos or manual adds. |
| ShelfBook | One book on one shelf, with its availability status and optional price. |
| Book | A canonical book record (ISBN, title, author, cover), shared across users. |
| Trade | An exchange of a book between users, by loan or sale. Not in v1. |
| Loan | A trade where the book returns to the owner. Not in v1. |
| Listing | A ShelfBook offered for Loan or Sale. Not in v1. |
| Recommendation | A suggested Book for a User. Not in v1. |

## AvailabilityStatus

```csharp
public enum AvailabilityStatus
{
    Private,
    Loan,
    Sale
}
```

## Entity sketches

These are sketches, not final code.

```csharp
public class User
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = "";
    public string City { get; set; } = "";
}

public class Shelf
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ICollection<ShelfBook> Books { get; set; } = new List<ShelfBook>();
}

public class Book
{
    public Guid Id { get; set; }
    public string? Isbn { get; set; }
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string? CoverUrl { get; set; }
}

public class ShelfBook
{
    public Guid ShelfId { get; set; }
    public Guid BookId { get; set; }
    public AvailabilityStatus Status { get; set; }
    public decimal? SalePrice { get; set; }
}
```

Later entities: `Trade`, `Loan`, `Listing`, `Recommendation`. Define them in `docs/requirements` before coding.

## Rules

- A Book exists once. ShelfBook links a Shelf to a Book.
- `SalePrice` is only meaningful when `Status` is `Sale`.
- New books default to `Private`.
- In v1 every ShelfBook is effectively Private; the shared public page shows the shelf only.

## Planned tables

```text
Users, Books, Shelves, ShelfBooks, Trades, Messages
```
