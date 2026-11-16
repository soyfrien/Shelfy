using Shelfy.Domain;

namespace Shelfy.Features.Lending;

/// <summary>
/// Viewer-specific, short labels for a loan's status: "Waiting on {name}" or "Waiting on you" instead of
/// a generic role like "the owner". Names are display names (see <see cref="Common.PublicUserName"/>).
/// </summary>
public static class LoanStatusLabels
{
    public static string For(Loan loan, string role, string ownerName, string? senderName = null) =>
        For(loan.Status, role, ownerName, senderName, senderIsOwner: loan.SenderId is null || loan.SenderId == loan.OwnerId);

    public static string For(LoanStatus status, string role, string ownerName, string? senderName = null, bool senderIsOwner = true)
    {
        var owner = Display(ownerName);
        var sender = Display(string.IsNullOrWhiteSpace(senderName) ? ownerName : senderName);
        return status switch
        {
            LoanStatus.Waitlisted => "On the waitlist",
            LoanStatus.Requested => WaitingOn(role == "owner", owner),
            LoanStatus.Approved => WaitingOn(role == "sender" || (role == "owner" && senderIsOwner), sender, " to mail it"),
            LoanStatus.Declined => "Declined",
            LoanStatus.Cancelled => "Cancelled",
            LoanStatus.Mailed => "In the mail",
            LoanStatus.Received => "Received",
            LoanStatus.ReturnRequested => role == "owner" ? "You asked for it back" : $"{owner} asked for it back",
            LoanStatus.ReturnMailed => WaitingOn(role == "owner", owner),
            LoanStatus.Returned => "Returned",
            LoanStatus.PassedOn => "Passed on to the next reader",
            _ => status.ToString()
        };
    }

    public static string WaitingOn(bool viewerIsThePerson, string name, string suffix = "") =>
        (viewerIsThePerson ? "Waiting on you" : "Waiting on " + Display(name)) + suffix;

    private static string Display(string? name) => string.IsNullOrWhiteSpace(name) ? "someone" : name.Trim();
}
