using Shelfy.Domain;
using Shelfy.Features.Lending;

namespace Shelfy.Tests.Features.Lending;

public class LoanStatusLabelsTests
{
    [Theory]
    [InlineData("borrower", "Waiting on Louis")]
    [InlineData("owner", "Waiting on you")]
    public void Requested_waits_on_the_owner(string role, string expected) =>
        Assert.Equal(expected, LoanStatusLabels.For(LoanStatus.Requested, role, "Louis"));

    [Theory]
    [InlineData("borrower", true, null, "Waiting on Louis to mail it")]
    [InlineData("owner", true, null, "Waiting on you to mail it")]
    [InlineData("sender", false, "Alice", "Waiting on you to mail it")]
    [InlineData("owner", false, "Alice", "Waiting on Alice to mail it")]
    [InlineData("borrower", false, "Alice", "Waiting on Alice to mail it")]
    public void Approved_waits_on_the_sender(string role, bool senderIsOwner, string? senderName, string expected) =>
        Assert.Equal(expected, LoanStatusLabels.For(LoanStatus.Approved, role, "Louis", senderName, senderIsOwner));

    [Theory]
    [InlineData("borrower", "Louis asked for it back")]
    [InlineData("owner", "You asked for it back")]
    public void Return_requested_names_the_owner(string role, string expected) =>
        Assert.Equal(expected, LoanStatusLabels.For(LoanStatus.ReturnRequested, role, "Louis"));

    [Theory]
    [InlineData("borrower", "Waiting on Louis")]
    [InlineData("owner", "Waiting on you")]
    public void Return_mailed_waits_on_the_owner(string role, string expected) =>
        Assert.Equal(expected, LoanStatusLabels.For(LoanStatus.ReturnMailed, role, "Louis"));

    [Fact]
    public void Other_statuses_stay_short_and_unchanged()
    {
        Assert.Equal("On the waitlist", LoanStatusLabels.For(LoanStatus.Waitlisted, "borrower", "Louis"));
        Assert.Equal("In the mail", LoanStatusLabels.For(LoanStatus.Mailed, "borrower", "Louis"));
        Assert.Equal("Received", LoanStatusLabels.For(LoanStatus.Received, "borrower", "Louis"));
        Assert.Equal("Declined", LoanStatusLabels.For(LoanStatus.Declined, "borrower", "Louis"));
        Assert.Equal("Passed on to the next reader", LoanStatusLabels.For(LoanStatus.PassedOn, "borrower", "Louis"));
    }
}
