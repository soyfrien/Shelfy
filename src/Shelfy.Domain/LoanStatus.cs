namespace Shelfy.Domain;

/// <summary>
/// States of a <see cref="Loan"/> (ADR-012). Happy path:
/// Requested -> Approved -> Mailed -> Received -> (ReturnRequested) -> ReturnMailed -> Returned.
/// A reservation starts as Waitlisted and becomes Requested when the copy is free (FIFO), or Approved directly when the owner
/// lets the current borrower pass the copy along; the earlier loan then ends as PassedOn once the copy is mailed on.
/// Declined, Cancelled, Returned and PassedOn are terminal.
/// </summary>
public enum LoanStatus
{
    Waitlisted,
    Requested,
    Approved,
    Declined,
    Cancelled,
    Mailed,
    Received,
    ReturnRequested,
    ReturnMailed,
    Returned,
    PassedOn
}
