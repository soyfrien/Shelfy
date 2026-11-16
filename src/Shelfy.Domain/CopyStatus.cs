namespace Shelfy.Domain;

/// <summary>Where a physical <see cref="Copy"/> is in its lending life (ADR-012).</summary>
public enum CopyStatus
{
    /// <summary>At its owner's home shelf and free to borrow.</summary>
    Available,

    /// <summary>Spoken for: a borrow request is open or approved but the copy has not been mailed yet.</summary>
    Reserved,

    /// <summary>With a borrower (received).</summary>
    OnLoan,

    /// <summary>In the mail, on no shelf.</summary>
    InTransit,

    /// <summary>The owner took it off lending.</summary>
    Unavailable
}
