namespace Shelfy.Domain;

/// <summary>
/// The lending state machine (ADR-012). Every step names the acting user and refuses with a <see cref="LendingException"/> when
/// that user may not take it or the state does not allow it. Steps mutate the given <see cref="Copy"/>, <see cref="Loan"/>s and
/// return any <see cref="CopyMovement"/> to add; persisting is the caller's job. <c>loans</c> is every loan of the copy
/// (open ones are enough).
///
/// The copy always belongs to its owner. Default flow: owner mails it, borrower receives it, borrower mails it back, owner
/// confirms the return; then the oldest reservation (if any) becomes a request. Pass-along (the borrower mails it straight to
/// the next reserver) needs <see cref="Copy.AllowPassAlong"/> and the owner's approval of each hand-off.
/// </summary>
public static class Lending
{
    public const int MaxMessageLength = 500;

    public static Loan RequestBorrow(Copy copy, IEnumerable<Loan> loans, Guid borrowerId, string? message, DateTime nowUtc)
    {
        CheckCanAsk(copy, loans, borrowerId);
        if (copy.Status != CopyStatus.Available)
        {
            throw LendingException.Conflict("This copy is not available right now. You can reserve it instead.");
        }

        var loan = NewLoan(copy, borrowerId, LoanStatus.Requested, message, nowUtc);
        loan.SenderId = copy.OwnerId;
        copy.Status = CopyStatus.Reserved;
        copy.Touch(nowUtc);
        return loan;
    }

    /// <summary>Joins the FIFO waitlist of a copy that is requested, lent out or in the mail.</summary>
    public static Loan Reserve(Copy copy, IEnumerable<Loan> loans, Guid borrowerId, string? message, DateTime nowUtc)
    {
        CheckCanAsk(copy, loans, borrowerId);
        if (copy.Status == CopyStatus.Available)
        {
            throw LendingException.Conflict("This copy is available: ask to borrow it instead of reserving.");
        }

        var loan = NewLoan(copy, borrowerId, LoanStatus.Waitlisted, message, nowUtc);
        copy.Touch(nowUtc);
        return loan;
    }

    public static void Approve(Loan loan, Copy copy, Guid actorId, DateTime nowUtc)
    {
        RequireOwner(loan, actorId);
        RequireStatus(loan, LoanStatus.Requested);
        loan.Status = LoanStatus.Approved;
        loan.ApprovedAt = nowUtc;
        Touch(loan, copy, nowUtc);
    }

    public static void Decline(Loan loan, Copy copy, IEnumerable<Loan> loans, Guid actorId, DateTime nowUtc)
    {
        RequireOwner(loan, actorId);
        RequireStatus(loan, LoanStatus.Requested);
        Close(loan, LoanStatus.Declined, nowUtc);
        Touch(loan, copy, nowUtc);
        ReleaseIfHome(copy, loans, nowUtc);
    }

    /// <summary>The borrower withdraws, or the owner calls it off, before the copy is mailed.</summary>
    public static void Cancel(Loan loan, Copy copy, IEnumerable<Loan> loans, Guid actorId, DateTime nowUtc)
    {
        if (actorId != loan.BorrowerId && actorId != loan.OwnerId)
        {
            throw LendingException.Forbidden("Only the borrower or the owner can cancel this request.");
        }

        RequireStatus(loan, LoanStatus.Waitlisted, LoanStatus.Requested, LoanStatus.Approved);
        var wasWaitlisted = loan.Status == LoanStatus.Waitlisted;
        Close(loan, LoanStatus.Cancelled, nowUtc);
        Touch(loan, copy, nowUtc);
        if (!wasWaitlisted && !loan.IsPassAlong)
        {
            ReleaseIfHome(copy, loans, nowUtc);
        }
    }

    /// <summary>The sender (owner, or previous borrower for a pass-along) has put the copy in the mail.</summary>
    public static CopyMovement MarkMailed(Loan loan, Copy copy, IEnumerable<Loan> loans, Guid actorId, DateTime nowUtc)
    {
        if (actorId != loan.SenderId)
        {
            throw LendingException.Forbidden("Only the person sending this copy can mark it as mailed.");
        }

        RequireStatus(loan, LoanStatus.Approved);
        if (copy.CurrentHolderId != actorId || copy.Status == CopyStatus.InTransit)
        {
            throw LendingException.Conflict("You don't have this copy at the moment.");
        }

        if (loan.IsPassAlong)
        {
            var previous = loans.FirstOrDefault(l => l.Id == loan.PreviousLoanId)
                ?? throw LendingException.Conflict("The loan this copy is passed on from is missing.");
            if (previous.Status is not (LoanStatus.Received or LoanStatus.ReturnRequested))
            {
                throw LendingException.Conflict("The previous loan is no longer with its borrower.");
            }

            Close(previous, LoanStatus.PassedOn, nowUtc);
        }

        var movement = Movement(copy, loan, loan.IsPassAlong ? CopyMovementKind.PassedAlong : CopyMovementKind.Lent, loan.BorrowerId, nowUtc);
        loan.Status = LoanStatus.Mailed;
        loan.MailedAt = nowUtc;
        copy.Status = CopyStatus.InTransit;
        copy.CurrentShelfId = null;
        Touch(loan, copy, nowUtc);
        return movement;
    }

    /// <summary>
    /// The borrower has the copy (typically by scanning its label at /c/{code}). It goes on <paramref name="shelfId"/>, which
    /// must be one of the borrower's shelves (checked by the caller), or on no shelf.
    /// </summary>
    public static void MarkReceived(Loan loan, Copy copy, IEnumerable<CopyMovement> movements, Guid actorId, Guid? shelfId, DateTime nowUtc)
    {
        if (actorId != loan.BorrowerId)
        {
            throw LendingException.Forbidden("Only the borrower can confirm receiving this copy.");
        }

        RequireStatus(loan, LoanStatus.Mailed);
        loan.Status = LoanStatus.Received;
        loan.ReceivedAt = nowUtc;
        copy.Status = CopyStatus.OnLoan;
        copy.CurrentHolderId = loan.BorrowerId;
        copy.CurrentShelfId = shelfId;
        CloseMovement(movements, loan, shelfId, nowUtc);
        Touch(loan, copy, nowUtc);
    }

    /// <summary>The owner asks for the copy back.</summary>
    public static void RequestReturn(Loan loan, Copy copy, Guid actorId, DateTime nowUtc)
    {
        RequireOwner(loan, actorId);
        RequireStatus(loan, LoanStatus.Received);
        loan.Status = LoanStatus.ReturnRequested;
        loan.ReturnRequestedAt = nowUtc;
        Touch(loan, copy, nowUtc);
    }

    /// <summary>The borrower has mailed the copy back to its owner (asked to or not).</summary>
    public static CopyMovement MarkReturnMailed(Loan loan, Copy copy, IEnumerable<Loan> loans, Guid actorId, DateTime nowUtc)
    {
        if (actorId != loan.BorrowerId)
        {
            throw LendingException.Forbidden("Only the borrower can mail this copy back.");
        }

        RequireStatus(loan, LoanStatus.Received, LoanStatus.ReturnRequested);
        if (loans.Any(l => l.PreviousLoanId == loan.Id && l.Status == LoanStatus.Approved))
        {
            throw LendingException.Conflict("The owner asked you to pass this copy on to the next reader instead.");
        }

        var movement = Movement(copy, loan, CopyMovementKind.Returned, loan.OwnerId, nowUtc);
        loan.Status = LoanStatus.ReturnMailed;
        loan.ReturnMailedAt = nowUtc;
        copy.Status = CopyStatus.InTransit;
        copy.CurrentShelfId = null;
        Touch(loan, copy, nowUtc);
        return movement;
    }

    /// <summary>The owner has the copy back: it returns to its home shelf and the next reservation (if any) becomes a request.</summary>
    public static void MarkReturned(Loan loan, Copy copy, IEnumerable<Loan> loans, IEnumerable<CopyMovement> movements, Guid actorId, DateTime nowUtc)
    {
        RequireOwner(loan, actorId);
        RequireStatus(loan, LoanStatus.ReturnMailed);
        Close(loan, LoanStatus.Returned, nowUtc);
        copy.CurrentHolderId = copy.OwnerId;
        copy.CurrentShelfId = copy.HomeShelfId;
        copy.Status = CopyStatus.Available;
        CloseMovement(movements, loan, copy.HomeShelfId, nowUtc);
        Touch(loan, copy, nowUtc);
        ReleaseIfHome(copy, loans, nowUtc);
    }

    /// <summary>
    /// The owner approves handing the copy from the current borrower straight to the first person on the waitlist. Needs
    /// <see cref="Copy.AllowPassAlong"/>. The next loan becomes Approved with the current borrower as sender; the current loan
    /// ends (PassedOn) when the copy is mailed on.
    /// </summary>
    public static Loan ApprovePassAlong(Loan current, Copy copy, IEnumerable<Loan> loans, Guid actorId, DateTime nowUtc)
    {
        RequireOwner(current, actorId);
        if (!copy.AllowPassAlong)
        {
            throw LendingException.Conflict("Pass-along is switched off for this copy; it goes back to you first.");
        }

        RequireStatus(current, LoanStatus.Received, LoanStatus.ReturnRequested);
        var all = loans.ToList();
        if (all.Any(l => l.PreviousLoanId == current.Id && l.IsOpen))
        {
            throw LendingException.Conflict("A hand-off from this loan is already approved.");
        }

        var next = NextInLine(all) ?? throw LendingException.Conflict("Nobody is waiting for this copy.");
        next.Status = LoanStatus.Approved;
        next.SenderId = current.BorrowerId;
        next.PreviousLoanId = current.Id;
        next.ApprovedAt = nowUtc;
        next.UpdatedAt = nowUtc;
        Touch(current, copy, nowUtc);
        return next;
    }

    /// <summary>The owner switches lending on or off for a copy that is at home with no open request.</summary>
    public static void SetAvailable(Copy copy, IEnumerable<Loan> loans, Guid actorId, bool available, DateTime nowUtc)
    {
        if (actorId != copy.OwnerId)
        {
            throw LendingException.Forbidden("Only the owner can change this copy.");
        }

        if (copy.Status is not (CopyStatus.Available or CopyStatus.Unavailable)
            || loans.Any(l => l.IsOpen && l.Status != LoanStatus.Waitlisted))
        {
            throw LendingException.Conflict("This copy is requested or lent out; finish or cancel that first.");
        }

        copy.Status = available ? CopyStatus.Available : CopyStatus.Unavailable;
        copy.Touch(nowUtc);
        if (available)
        {
            ReleaseIfHome(copy, loans, nowUtc);
        }
    }

    /// <summary>The oldest waiting reservation (FIFO by creation time).</summary>
    public static Loan? NextInLine(IEnumerable<Loan> loans) =>
        loans.Where(l => l.Status == LoanStatus.Waitlisted).OrderBy(l => l.CreatedAt).ThenBy(l => l.Id).FirstOrDefault();

    /// <summary>
    /// When the copy is home and nothing is in progress, the next reservation becomes a request to the owner; otherwise the
    /// copy is simply available again.
    /// </summary>
    private static void ReleaseIfHome(Copy copy, IEnumerable<Loan> loans, DateTime nowUtc)
    {
        var all = loans.ToList();
        if (copy.CurrentHolderId != copy.OwnerId || copy.Status is CopyStatus.Unavailable or CopyStatus.InTransit
            || all.Any(l => l.IsOpen && l.Status != LoanStatus.Waitlisted))
        {
            return;
        }

        var next = NextInLine(all);
        if (next is null)
        {
            copy.Status = CopyStatus.Available;
            return;
        }

        next.Status = LoanStatus.Requested;
        next.SenderId = copy.OwnerId;
        next.UpdatedAt = nowUtc;
        copy.Status = CopyStatus.Reserved;
    }

    private static void CheckCanAsk(Copy copy, IEnumerable<Loan> loans, Guid borrowerId)
    {
        if (borrowerId == copy.OwnerId)
        {
            throw LendingException.Forbidden("This is your own copy.");
        }

        if (copy.Status == CopyStatus.Unavailable)
        {
            throw LendingException.Conflict("The owner isn't lending this copy right now.");
        }

        if (copy.CurrentHolderId == borrowerId && copy.Status == CopyStatus.OnLoan)
        {
            throw LendingException.Conflict("You already have this copy.");
        }

        if (loans.Any(l => l.BorrowerId == borrowerId && l.IsOpen))
        {
            throw LendingException.Conflict("You already have a request or reservation for this copy.");
        }
    }

    private static Loan NewLoan(Copy copy, Guid borrowerId, LoanStatus status, string? message, DateTime nowUtc)
    {
        message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (message is { Length: > MaxMessageLength })
        {
            throw new LendingException(LendingError.Invalid, $"The message can be at most {MaxMessageLength} characters.");
        }

        return new Loan
        {
            Id = Guid.NewGuid(),
            CopyId = copy.Id,
            OwnerId = copy.OwnerId,
            BorrowerId = borrowerId,
            Status = status,
            Message = message,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        };
    }

    private static CopyMovement Movement(Copy copy, Loan loan, CopyMovementKind kind, Guid toUserId, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(),
        CopyId = copy.Id,
        LoanId = loan.Id,
        Kind = kind,
        FromUserId = copy.CurrentHolderId,
        FromShelfId = copy.CurrentShelfId,
        ToUserId = toUserId,
        StartedAt = nowUtc
    };

    private static void CloseMovement(IEnumerable<CopyMovement> movements, Loan loan, Guid? shelfId, DateTime nowUtc)
    {
        var open = movements.Where(m => m.LoanId == loan.Id && m.InTransit).OrderByDescending(m => m.StartedAt).FirstOrDefault();
        if (open is not null)
        {
            open.ReceivedAt = nowUtc;
            open.ToShelfId = shelfId;
        }
    }

    private static void RequireOwner(Loan loan, Guid actorId)
    {
        if (actorId != loan.OwnerId)
        {
            throw LendingException.Forbidden("Only the owner of this copy can do that.");
        }
    }

    private static void RequireStatus(Loan loan, params LoanStatus[] allowed)
    {
        if (!allowed.Contains(loan.Status))
        {
            throw LendingException.Conflict($"This loan is {loan.Status}; that step isn't possible now.");
        }
    }

    private static void Close(Loan loan, LoanStatus status, DateTime nowUtc)
    {
        loan.Status = status;
        loan.ClosedAt = nowUtc;
        loan.UpdatedAt = nowUtc;
    }

    private static void Touch(Loan loan, Copy copy, DateTime nowUtc)
    {
        loan.UpdatedAt = nowUtc;
        copy.Touch(nowUtc);
    }
}
