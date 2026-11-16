namespace Shelfy.Domain;

/// <summary>
/// A request to borrow one <see cref="Copy"/>, from reservation to return (ADR-012). State changes go through
/// <see cref="Lending"/>, which checks who may make each step. All timestamps are UTC.
/// </summary>
public class Loan
{
    public Guid Id { get; set; }
    public Guid CopyId { get; set; }
    public Copy? Copy { get; set; }

    /// <summary>The copy's owner: approves, declines, asks for it back and confirms the return.</summary>
    public Guid OwnerId { get; set; }

    public Guid BorrowerId { get; set; }

    /// <summary>
    /// Who mails the copy to the borrower: the owner, or for a pass-along the previous borrower. Set when the loan becomes
    /// Requested/Approved; null while Waitlisted.
    /// </summary>
    public Guid? SenderId { get; set; }

    /// <summary>For a pass-along: the loan whose borrower mails the copy on.</summary>
    public Guid? PreviousLoanId { get; set; }

    public LoanStatus Status { get; set; }

    /// <summary>Optional note from the borrower to the owner.</summary>
    public string? Message { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? MailedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public DateTime? ReturnRequestedAt { get; set; }
    public DateTime? ReturnMailedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public static readonly LoanStatus[] Terminal = [LoanStatus.Declined, LoanStatus.Cancelled, LoanStatus.Returned, LoanStatus.PassedOn];

    /// <summary>
    /// The states in which the parties may see each other's mailing address (approved and not finished).
    /// </summary>
    public static readonly LoanStatus[] AddressesShared =
        [LoanStatus.Approved, LoanStatus.Mailed, LoanStatus.Received, LoanStatus.ReturnRequested, LoanStatus.ReturnMailed];

    public bool IsOpen => !Terminal.Contains(Status);

    public bool IsPassAlong => PreviousLoanId is not null;
}
