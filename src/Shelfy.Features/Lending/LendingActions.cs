using Shelfy.Domain;

namespace Shelfy.Features.Lending;

/// <summary>
/// Which steps of the state machine (<see cref="Lending"/>) a given user can take on a loan right now. Used to show buttons;
/// the state machine itself still checks every step.
/// </summary>
public static class LendingActions
{
    public const string Approve = "approve", Decline = "decline", Cancel = "cancel", Mailed = "mailed", Received = "received",
        RequestReturn = "request-return", ReturnMailed = "return-mailed", Returned = "returned", PassAlong = "pass-along";

    public static IReadOnlyList<string> For(Loan loan, Copy copy, IReadOnlyCollection<Loan> loans, Guid? userId)
    {
        var a = new List<string>();
        if (userId is not Guid me)
        {
            return a;
        }

        var owner = me == loan.OwnerId;
        var borrower = me == loan.BorrowerId;
        switch (loan.Status)
        {
            case LoanStatus.Waitlisted:
                if (owner || borrower) a.Add(Cancel);
                break;
            case LoanStatus.Requested:
                if (owner) { a.Add(Approve); a.Add(Decline); }
                if (borrower) a.Add(Cancel);
                break;
            case LoanStatus.Approved:
                if (me == loan.SenderId && copy.CurrentHolderId == me && copy.Status != CopyStatus.InTransit) a.Add(Mailed);
                if (owner || borrower) a.Add(Cancel);
                break;
            case LoanStatus.Mailed:
                if (borrower) a.Add(Received);
                break;
            case LoanStatus.Received:
            case LoanStatus.ReturnRequested:
                var handOff = loans.Any(l => l.PreviousLoanId == loan.Id && l.IsOpen);
                if (owner && loan.Status == LoanStatus.Received) a.Add(RequestReturn);
                if (owner && copy.AllowPassAlong && !handOff && Domain.Lending.NextInLine(loans) is not null) a.Add(PassAlong);
                if (borrower && !handOff) a.Add(ReturnMailed);
                break;
            case LoanStatus.ReturnMailed:
                if (owner) a.Add(Returned);
                break;
        }

        return a;
    }
}
