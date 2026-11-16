namespace Shelfy.Domain;

/// <summary>
/// One physical book (ADR-012). A <see cref="Book"/> is the title/edition (keyed by ISBN) and exists once; a Copy is the object
/// on somebody's shelf that can be lent and mailed and keeps its identity as it moves from shelf to shelf.
/// All timestamps are UTC. State changes go through <see cref="Lending"/>.
/// </summary>
public class Copy
{
    /// <summary>Permanent identity. The owner never changes through lending: a copy is always its owner's, only lent out.</summary>
    public Guid Id { get; set; }

    /// <summary>Short unique code for stickers and QR labels, see <see cref="CopyCode"/>.</summary>
    public string Code { get; set; } = "";

    public Guid BookId { get; set; }
    public Book? Book { get; set; }

    public Guid OwnerId { get; set; }

    /// <summary>The owner's shelf the copy lives on and returns to. Null if that shelf was deleted.</summary>
    public Guid? HomeShelfId { get; set; }

    /// <summary>Who physically has it (or last had it, while it is in the mail).</summary>
    public Guid CurrentHolderId { get; set; }

    /// <summary>The shelf it is on now; null while in transit (or when the holder has no shelf).</summary>
    public Guid? CurrentShelfId { get; set; }

    public CopyStatus Status { get; set; } = CopyStatus.Available;

    /// <summary>
    /// The owner lets a borrower mail the copy straight on to the next person on the waitlist (each hand-off still needs the
    /// owner's approval). Off by default: copies go back to their owner after every loan.
    /// </summary>
    public bool AllowPassAlong { get; set; }

    public string? Condition { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency token: every lending step bumps it, so two racing steps on one copy cannot both win.</summary>
    public int Version { get; set; }

    public static Copy Register(Guid bookId, Guid ownerId, Guid? shelfId, string code, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        BookId = bookId,
        OwnerId = ownerId,
        HomeShelfId = shelfId,
        CurrentHolderId = ownerId,
        CurrentShelfId = shelfId,
        Status = CopyStatus.Available,
        CreatedAt = nowUtc,
        UpdatedAt = nowUtc
    };

    internal void Touch(DateTime nowUtc)
    {
        UpdatedAt = nowUtc;
        Version++;
    }
}
