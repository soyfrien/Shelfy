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
| Copy | One physical book: permanent Id plus a short sticker code (`7K3M-Q9XA`). Always its owner's; has a home shelf and a current holder/shelf. ADR-012. |
| CopyMovement | One step in a copy's journey (registered, lent, passed along, returned), with mailed and received times. ADR-012. |
| Loan | A request to borrow one Copy, from waitlist or request through mailing and receipt to return (or pass-along). The book returns to its owner by default. ADR-012. |
| Listing | A ShelfBook offered for Loan or Sale. Not in v1. |
| Recommendation | A suggested Book for a User. Not in v1. |
| RecognitionJob | One uploaded shelf photo and its processing state (Pending, Processing, Completed, Failed). Recognition module, ADR-008. |
| DetectedSpine | One book spine found in a photo: raw OCR text, parsed title/author candidate and whether it resolved to a Book (by a catalog, a remembered correction, or the user). It can be dismissed ("not a book"). Recognition module, ADR-008, ADR-011. |
| UserCorrection | A remembered fix: a normalized spine text mapped to a Book, consulted before the catalogs. Global until accounts exist. ADR-011. |

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
    public bool IsHidden { get; set; }       // soft-cleared (ADR-010); HiddenAt (UTC) says when
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

Recognition entities (see ADR-008 and `docs/pipeline.md`):

```csharp
public class RecognitionJob   // ShelfId, PhotoKey, Status, Attempts, MaxAttempts, NextAttemptAt, Error, BooksAdded, ...
public class DetectedSpine    // RecognitionJobId, Position, RawText, Confidence, CandidateTitle/Author/Isbn,
                              // Status (Unresolved|Resolved), BookId, ResolvedIsbn, MatchScore, MatchSource
```

`Copy`, `CopyMovement` and `Loan` exist since ADR-012 (see the state table there). Later entities: `Trade`, `Listing`, `Recommendation`. Define them in `docs/requirements` before coding.

## Rules

- A Book exists once. ShelfBook links a Shelf to a Book.
- `SalePrice` is only meaningful when `Status` is `Sale`.
- New books default to `Private`.
- A hidden shelf is treated as not existing by the public API; deleting a shelf removes its links, jobs, spines and photos but never `Books` (ADR-010).
- In v1 every ShelfBook is effectively Private; the shared public page shows the shelf only.

## Planned tables

```text
Users, Books, Shelves, ShelfBooks, RecognitionJobs, DetectedSpines, Trades, Messages
```
