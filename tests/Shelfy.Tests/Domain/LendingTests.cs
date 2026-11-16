using Shelfy.Domain;

namespace Shelfy.Tests.Domain;

/// <summary>The lending state machine (ADR-012): every transition, who may take it, waitlist order and pass-along.</summary>
public class LendingTests
{
    private static readonly DateTime T0 = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _owner = Guid.NewGuid(), _alice = Guid.NewGuid(), _bob = Guid.NewGuid(), _carol = Guid.NewGuid(), _stranger = Guid.NewGuid();
    private readonly Guid _home = Guid.NewGuid(), _aliceShelf = Guid.NewGuid(), _bobShelf = Guid.NewGuid();
    private readonly Copy _copy;
    private readonly List<Loan> _loans = new();
    private readonly List<CopyMovement> _moves = new();
    private DateTime _now = T0;

    public LendingTests() => _copy = Copy.Register(Guid.NewGuid(), _owner, _home, "7K3MQ9XA", T0);

    private DateTime Tick() => _now = _now.AddMinutes(1);

    private Loan Borrow(Guid who)
    {
        var loan = Lending.RequestBorrow(_copy, _loans, who, null, Tick());
        _loans.Add(loan);
        return loan;
    }

    private Loan Reserve(Guid who)
    {
        var loan = Lending.Reserve(_copy, _loans, who, null, Tick());
        _loans.Add(loan);
        return loan;
    }

    private void Mail(Loan loan, Guid sender) => _moves.Add(Lending.MarkMailed(loan, _copy, _loans, sender, Tick()));
    private void ReturnMail(Loan loan) => _moves.Add(Lending.MarkReturnMailed(loan, _copy, _loans, loan.BorrowerId, Tick()));

    /// <summary>Alice borrows, receives, and is holding the copy on her shelf.</summary>
    private Loan AliceHasIt()
    {
        var loan = Borrow(_alice);
        Lending.Approve(loan, _copy, _owner, Tick());
        Mail(loan, _owner);
        Lending.MarkReceived(loan, _copy, _moves, _alice, _aliceShelf, Tick());
        return loan;
    }

    private static void Refused(LendingError error, Action step) => Assert.Equal(error, Assert.Throws<LendingException>(step).Error);

    [Fact]
    public void Happy_path_return_to_owner_moves_the_copy_and_records_each_step()
    {
        var loan = Borrow(_alice);
        Assert.Equal(LoanStatus.Requested, loan.Status);
        Assert.Equal(CopyStatus.Reserved, _copy.Status);
        Assert.Equal(_owner, loan.SenderId);

        Lending.Approve(loan, _copy, _owner, Tick());
        Assert.Equal(LoanStatus.Approved, loan.Status);

        Mail(loan, _owner);
        Assert.Equal(LoanStatus.Mailed, loan.Status);
        Assert.Equal(CopyStatus.InTransit, _copy.Status);
        Assert.Null(_copy.CurrentShelfId);
        Assert.True(_moves.Single().InTransit);
        Assert.Equal(CopyMovementKind.Lent, _moves[0].Kind);
        Assert.Equal(_home, _moves[0].FromShelfId);

        Lending.MarkReceived(loan, _copy, _moves, _alice, _aliceShelf, Tick());
        Assert.Equal(CopyStatus.OnLoan, _copy.Status);
        Assert.Equal(_alice, _copy.CurrentHolderId);
        Assert.Equal(_aliceShelf, _copy.CurrentShelfId);
        Assert.Equal(_aliceShelf, _moves[0].ToShelfId);
        Assert.NotNull(_moves[0].ReceivedAt);

        Lending.RequestReturn(loan, _copy, _owner, Tick());
        Assert.Equal(LoanStatus.ReturnRequested, loan.Status);

        ReturnMail(loan);
        Assert.Equal(LoanStatus.ReturnMailed, loan.Status);
        Assert.Equal(CopyStatus.InTransit, _copy.Status);
        Assert.Equal(CopyMovementKind.Returned, _moves[1].Kind);
        Assert.Equal(_alice, _moves[1].FromUserId);
        Assert.Equal(_owner, _moves[1].ToUserId);

        Lending.MarkReturned(loan, _copy, _loans, _moves, _owner, Tick());
        Assert.Equal(LoanStatus.Returned, loan.Status);
        Assert.NotNull(loan.ClosedAt);
        Assert.Equal(CopyStatus.Available, _copy.Status);
        Assert.Equal(_owner, _copy.CurrentHolderId);
        Assert.Equal(_home, _copy.CurrentShelfId);
        Assert.All(_moves, m => Assert.False(m.InTransit));
        Assert.Equal(_owner, _copy.OwnerId);   // a copy always stays its owner's
    }

    [Fact]
    public void Borrower_can_mail_it_back_without_being_asked()
    {
        var loan = AliceHasIt();
        ReturnMail(loan);
        Assert.Equal(LoanStatus.ReturnMailed, loan.Status);
    }

    [Fact]
    public void Each_step_is_refused_for_the_wrong_person()
    {
        Refused(LendingError.Forbidden, () => Lending.RequestBorrow(_copy, _loans, _owner, null, Tick()));
        var loan = Borrow(_alice);
        Refused(LendingError.Forbidden, () => Lending.Approve(loan, _copy, _alice, Tick()));
        Refused(LendingError.Forbidden, () => Lending.Approve(loan, _copy, _stranger, Tick()));
        Refused(LendingError.Forbidden, () => Lending.Decline(loan, _copy, _loans, _alice, Tick()));
        Refused(LendingError.Forbidden, () => Lending.Cancel(loan, _copy, _loans, _stranger, Tick()));
        Lending.Approve(loan, _copy, _owner, Tick());
        Refused(LendingError.Forbidden, () => Lending.MarkMailed(loan, _copy, _loans, _alice, Tick()));
        Mail(loan, _owner);
        Refused(LendingError.Forbidden, () => Lending.MarkReceived(loan, _copy, _moves, _owner, null, Tick()));
        Refused(LendingError.Forbidden, () => Lending.MarkReceived(loan, _copy, _moves, _stranger, null, Tick()));
        Lending.MarkReceived(loan, _copy, _moves, _alice, null, Tick());
        Refused(LendingError.Forbidden, () => Lending.RequestReturn(loan, _copy, _alice, Tick()));
        Refused(LendingError.Forbidden, () => Lending.MarkReturnMailed(loan, _copy, _loans, _owner, Tick()));
        ReturnMail(loan);
        Refused(LendingError.Forbidden, () => Lending.MarkReturned(loan, _copy, _loans, _moves, _alice, Tick()));
        Refused(LendingError.Forbidden, () => Lending.SetAvailable(_copy, _loans, _alice, false, Tick()));
    }

    [Fact]
    public void Steps_out_of_order_are_conflicts()
    {
        var loan = Borrow(_alice);
        Refused(LendingError.Conflict, () => Lending.MarkMailed(loan, _copy, _loans, _owner, Tick()));
        Refused(LendingError.Conflict, () => Lending.MarkReceived(loan, _copy, _moves, _alice, null, Tick()));
        Refused(LendingError.Conflict, () => Lending.RequestReturn(loan, _copy, _owner, Tick()));
        Refused(LendingError.Conflict, () => Lending.MarkReturned(loan, _copy, _loans, _moves, _owner, Tick()));
        Lending.Approve(loan, _copy, _owner, Tick());
        Refused(LendingError.Conflict, () => Lending.Approve(loan, _copy, _owner, Tick()));
        Mail(loan, _owner);
        Refused(LendingError.Conflict, () => Lending.Cancel(loan, _copy, _loans, _alice, Tick()));   // too late once mailed
        Refused(LendingError.Conflict, () => Lending.Decline(loan, _copy, _loans, _owner, Tick()));
    }

    [Fact]
    public void Asking_twice_or_for_an_unavailable_copy_is_refused()
    {
        Borrow(_alice);
        Refused(LendingError.Conflict, () => Lending.RequestBorrow(_copy, _loans, _bob, null, Tick()));   // requested: reserve instead
        Refused(LendingError.Conflict, () => Lending.Reserve(_copy, _loans, _alice, null, Tick()));       // already asked
        Reserve(_bob);
        Refused(LendingError.Conflict, () => Lending.Reserve(_copy, _loans, _bob, null, Tick()));
    }

    [Fact]
    public void Reserving_an_available_copy_is_refused_and_unavailable_copies_cannot_be_asked_for()
    {
        Refused(LendingError.Conflict, () => Lending.Reserve(_copy, _loans, _alice, null, Tick()));
        Lending.SetAvailable(_copy, _loans, _owner, false, Tick());
        Assert.Equal(CopyStatus.Unavailable, _copy.Status);
        Refused(LendingError.Conflict, () => Lending.RequestBorrow(_copy, _loans, _alice, null, Tick()));
        Lending.SetAvailable(_copy, _loans, _owner, true, Tick());
        Assert.Equal(CopyStatus.Available, _copy.Status);
    }

    [Fact]
    public void Waitlist_is_first_in_first_out_and_the_next_reservation_becomes_a_request_on_return()
    {
        var first = AliceHasIt();
        var bob = Reserve(_bob);
        var carol = Reserve(_carol);
        Assert.Equal(LoanStatus.Waitlisted, bob.Status);
        Assert.Null(bob.SenderId);

        ReturnMail(first);
        Lending.MarkReturned(first, _copy, _loans, _moves, _owner, Tick());

        Assert.Equal(LoanStatus.Requested, bob.Status);
        Assert.Equal(_owner, bob.SenderId);
        Assert.Equal(LoanStatus.Waitlisted, carol.Status);
        Assert.Equal(CopyStatus.Reserved, _copy.Status);
        Assert.Equal(_home, _copy.CurrentShelfId);   // back home first: return-to-owner is the default

        Lending.Decline(bob, _copy, _loans, _owner, Tick());
        Assert.Equal(LoanStatus.Declined, bob.Status);
        Assert.Equal(LoanStatus.Requested, carol.Status);

        Lending.Cancel(carol, _copy, _loans, _carol, Tick());
        Assert.Equal(CopyStatus.Available, _copy.Status);
    }

    [Fact]
    public void Cancelling_a_waitlist_place_does_not_touch_the_current_loan()
    {
        var first = AliceHasIt();
        var bob = Reserve(_bob);
        Lending.Cancel(bob, _copy, _loans, _bob, Tick());
        Assert.Equal(LoanStatus.Cancelled, bob.Status);
        Assert.Equal(LoanStatus.Received, first.Status);
        Assert.Equal(CopyStatus.OnLoan, _copy.Status);
    }

    [Fact]
    public void Pass_along_is_refused_unless_the_owner_allows_it_for_this_copy()
    {
        var first = AliceHasIt();
        Reserve(_bob);
        Refused(LendingError.Conflict, () => Lending.ApprovePassAlong(first, _copy, _loans, _owner, Tick()));
    }

    [Fact]
    public void Pass_along_needs_the_owner_and_hands_the_copy_from_borrower_to_borrower()
    {
        _copy.AllowPassAlong = true;
        var first = AliceHasIt();
        var bob = Reserve(_bob);
        Refused(LendingError.Forbidden, () => Lending.ApprovePassAlong(first, _copy, _loans, _alice, Tick()));

        var next = Lending.ApprovePassAlong(first, _copy, _loans, _owner, Tick());
        Assert.Same(bob, next);
        Assert.Equal(LoanStatus.Approved, bob.Status);
        Assert.Equal(_alice, bob.SenderId);
        Assert.Equal(first.Id, bob.PreviousLoanId);
        Refused(LendingError.Conflict, () => Lending.ApprovePassAlong(first, _copy, _loans, _owner, Tick()));   // once
        Refused(LendingError.Conflict, () => Lending.MarkReturnMailed(first, _copy, _loans, _alice, Tick()));   // pass it on instead
        Refused(LendingError.Forbidden, () => Lending.MarkMailed(bob, _copy, _loans, _owner, Tick()));         // Alice has it, not the owner

        Mail(bob, _alice);
        Assert.Equal(LoanStatus.PassedOn, first.Status);
        Assert.Equal(CopyMovementKind.PassedAlong, _moves.Last().Kind);
        Assert.Equal(_aliceShelf, _moves.Last().FromShelfId);

        Lending.MarkReceived(bob, _copy, _moves, _bob, _bobShelf, Tick());
        Assert.Equal(_bob, _copy.CurrentHolderId);
        Assert.Equal(_bobShelf, _copy.CurrentShelfId);
        Assert.Equal(_owner, _copy.OwnerId);
        Assert.Equal(_home, _copy.HomeShelfId);

        // Bob returns it to the owner, not to Alice.
        ReturnMail(bob);
        Assert.Equal(_owner, _moves.Last().ToUserId);
        Lending.MarkReturned(bob, _copy, _loans, _moves, _owner, Tick());
        Assert.Equal(_home, _copy.CurrentShelfId);
    }

    [Fact]
    public void A_cancelled_pass_along_leaves_the_copy_with_the_current_borrower()
    {
        _copy.AllowPassAlong = true;
        var first = AliceHasIt();
        var bob = Reserve(_bob);
        Lending.ApprovePassAlong(first, _copy, _loans, _owner, Tick());
        Lending.Cancel(bob, _copy, _loans, _bob, Tick());
        Assert.Equal(LoanStatus.Received, first.Status);
        Assert.Equal(CopyStatus.OnLoan, _copy.Status);
        ReturnMail(first);   // and Alice can now send it home
        Assert.Equal(LoanStatus.ReturnMailed, first.Status);
    }

    [Fact]
    public void Every_step_bumps_the_copy_version_for_optimistic_concurrency()
    {
        var v = _copy.Version;
        var loan = Borrow(_alice);
        Assert.True(_copy.Version > v);
        v = _copy.Version;
        Lending.Approve(loan, _copy, _owner, Tick());
        Assert.True(_copy.Version > v);
    }

    [Fact]
    public void Long_messages_are_invalid()
    {
        var ex = Assert.Throws<LendingException>(() => Lending.RequestBorrow(_copy, _loans, _alice, new string('x', 501), Tick()));
        Assert.Equal(LendingError.Invalid, ex.Error);
    }
}
