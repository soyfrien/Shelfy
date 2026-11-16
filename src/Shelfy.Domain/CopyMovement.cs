using System.Text.Json.Serialization;

namespace Shelfy.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CopyMovementKind
{
    /// <summary>The copy was registered on its owner's shelf.</summary>
    Registered,

    /// <summary>Mailed to a borrower by its owner.</summary>
    Lent,

    /// <summary>Mailed by one borrower straight to the next one (pass-along).</summary>
    PassedAlong,

    /// <summary>Mailed back to its owner.</summary>
    Returned
}

/// <summary>
/// One step in a copy's journey (ADR-012): who sent it where, and when it was mailed and received. A row is opened when the copy
/// is mailed (<see cref="ReceivedAt"/> null = in the mail) and closed when the recipient confirms receipt. Never deleted.
/// </summary>
public class CopyMovement
{
    public Guid Id { get; set; }
    public Guid CopyId { get; set; }
    public Guid? LoanId { get; set; }
    public CopyMovementKind Kind { get; set; }

    public Guid? FromUserId { get; set; }
    public Guid? FromShelfId { get; set; }
    public Guid ToUserId { get; set; }

    /// <summary>Set when received (the shelf the recipient put it on).</summary>
    public Guid? ToShelfId { get; set; }

    /// <summary>When it left (for <see cref="CopyMovementKind.Registered"/>: when it was registered).</summary>
    public DateTime StartedAt { get; set; }

    public DateTime? ReceivedAt { get; set; }

    public bool InTransit => ReceivedAt is null && Kind != CopyMovementKind.Registered;
}
