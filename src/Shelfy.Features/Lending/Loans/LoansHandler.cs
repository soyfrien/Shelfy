using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Lending.Loans;

/// <summary>
/// Borrowing and reserving copies and every later step of a loan (ADR-012). The steps themselves live in
/// <see cref="Domain.Lending"/>; this handler loads the copy with its loans, checks the extra rules that need the database
/// (mailing addresses, the recipient's shelf), saves with optimistic concurrency and shapes the responses.
/// </summary>
public class LoansHandler(ShelfyDbContext db, TimeProvider clock)
{
    public Task<HandlerResult<LoanDto>> BorrowAsync(Guid? me, Guid copyId, AskRequest request, CancellationToken ct = default) =>
        AskAsync(me, copyId, request, reserve: false, ct);

    public Task<HandlerResult<LoanDto>> ReserveAsync(Guid? me, Guid copyId, AskRequest request, CancellationToken ct = default) =>
        AskAsync(me, copyId, request, reserve: true, ct);

    private async Task<HandlerResult<LoanDto>> AskAsync(Guid? me, Guid copyId, AskRequest request, bool reserve, CancellationToken ct)
    {
        if (me is null)
        {
            return HandlerResult<LoanDto>.Unauthorized("Sign in to borrow books.");
        }

        var agg = await LendingQueries.LoadAsync(db, copyId, ct);
        if (agg is null)
        {
            return HandlerResult<LoanDto>.NotFound("Copy not found.");
        }

        if (!await HasConfirmedEmailAsync(me.Value, ct))
        {
            return HandlerResult<LoanDto>.Invalid(ConfirmEmailFirst);
        }

        if (!reserve && !await HasAddressAsync(me.Value, ct))
        {
            var ownerName = await NameOfAsync(agg.Copy.OwnerId, ct);
            return HandlerResult<LoanDto>.Invalid($"Add your mailing address to your profile first, so {ownerName} can send you the book.");
        }

        var now = Now;
        Loan loan;
        try
        {
            loan = reserve
                ? Domain.Lending.Reserve(agg.Copy, agg.Loans, me.Value, request.Message, now)
                : Domain.Lending.RequestBorrow(agg.Copy, agg.Loans, me.Value, request.Message, now);
        }
        catch (LendingException ex)
        {
            return HandlerResult<LoanDto>.From(ex.WithOwnerName(await NameOfAsync(agg.Copy.OwnerId, ct)));
        }

        db.Loans.Add(loan);
        agg.Loans.Add(loan);
        return await SaveAsync(me.Value, agg, loan, ct);
    }

    /// <summary>One step on an existing loan, by action name (see <see cref="LendingActions"/>).</summary>
    internal const string EnterStickerCode = "Enter the code on the book's sticker to confirm it arrived.";

    public async Task<HandlerResult<LoanDto>> ActAsync(Guid? me, Guid loanId, string action, ReceiveRequest? body = null, CancellationToken ct = default)
    {
        if (me is null)
        {
            return HandlerResult<LoanDto>.Unauthorized("Sign in first.");
        }

        var copyId = await db.Loans.Where(l => l.Id == loanId).Select(l => (Guid?)l.CopyId).FirstOrDefaultAsync(ct);
        var agg = copyId is null ? null : await LendingQueries.LoadAsync(db, copyId.Value, ct);
        var loan = agg?.Loans.FirstOrDefault(l => l.Id == loanId);
        if (agg is null || loan is null || !IsParty(loan, me.Value))
        {
            // Not a party: indistinguishable from an unknown loan.
            return HandlerResult<LoanDto>.NotFound("Loan not found.");
        }

        var (copy, loans, movements) = agg;
        var now = Now;
        try
        {
            switch (action)
            {
                case LendingActions.Approve:
                    if (!await HasConfirmedEmailAsync(me.Value, ct))
                    {
                        return HandlerResult<LoanDto>.Invalid(ConfirmEmailFirst);
                    }

                    if (!await HasAddressAsync(me.Value, ct))
                    {
                        return HandlerResult<LoanDto>.Invalid("Add your mailing address to your profile first, so the book can come back to you.");
                    }

                    // A reservation needs no address, and the waitlist can promote it to a request: check the reader's address now.
                    if (!await HasAddressAsync(loan.BorrowerId, ct))
                    {
                        return HandlerResult<LoanDto>.Invalid(ReaderHasNoAddress);
                    }

                    Domain.Lending.Approve(loan, copy, me.Value, now);
                    break;
                case LendingActions.Decline:
                    Domain.Lending.Decline(loan, copy, loans, me.Value, now);
                    break;
                case LendingActions.Cancel:
                    Domain.Lending.Cancel(loan, copy, loans, me.Value, now);
                    break;
                case LendingActions.Mailed:
                    db.CopyMovements.Add(Domain.Lending.MarkMailed(loan, copy, loans, me.Value, now));
                    break;
                case LendingActions.Received:
                    // The sticker code proves the book actually arrived; without it anyone party to the loan could mark it received.
                    if (string.IsNullOrWhiteSpace(body?.Code))
                    {
                        return HandlerResult<LoanDto>.Invalid(EnterStickerCode);
                    }

                    if (CopyCode.Normalize(body.Code) != copy.Code)
                    {
                        return HandlerResult<LoanDto>.Invalid("That code doesn't match this book's sticker. Check the code and try again.");
                    }

                    var shelfId = body.ShelfId;
                    if (shelfId is not null && !await db.Shelves.AnyAsync(s => s.Id == shelfId && s.UserId == me && !s.IsHidden, ct))
                    {
                        return HandlerResult<LoanDto>.Invalid("Choose one of your own shelves.");
                    }

                    Domain.Lending.MarkReceived(loan, copy, movements, me.Value, shelfId, now);
                    break;
                case LendingActions.RequestReturn:
                    Domain.Lending.RequestReturn(loan, copy, me.Value, now);
                    break;
                case LendingActions.ReturnMailed:
                    db.CopyMovements.Add(Domain.Lending.MarkReturnMailed(loan, copy, loans, me.Value, now));
                    break;
                case LendingActions.Returned:
                    Domain.Lending.MarkReturned(loan, copy, loans, movements, me.Value, now);
                    break;
                case LendingActions.PassAlong:
                    var nextReader = Domain.Lending.NextInLine(loans);
                    if (nextReader is not null && !await HasAddressAsync(nextReader.BorrowerId, ct))
                    {
                        return HandlerResult<LoanDto>.Invalid(ReaderHasNoAddress);
                    }

                    Domain.Lending.ApprovePassAlong(loan, copy, loans, me.Value, now);
                    break;
                default:
                    return HandlerResult<LoanDto>.NotFound("Unknown action.");
            }
        }
        catch (LendingException ex)
        {
            return HandlerResult<LoanDto>.From(ex.WithOwnerName(await NameOfAsync(loan.OwnerId, ct)));
        }

        return await SaveAsync(me.Value, agg, loan, ct);
    }

    /// <summary>
    /// "I received this" from the copy's label page (/c/{code}): the caller's in-transit loan of that copy is marked received
    /// (borrower), or the copy's return is confirmed (owner, when it comes home).
    /// </summary>
    public async Task<HandlerResult<LoanDto>> ReceiveByCodeAsync(Guid? me, string code, ReceiveRequest body, CancellationToken ct = default)
    {
        if (me is null)
        {
            return HandlerResult<LoanDto>.Unauthorized("Sign in to confirm you received this book.");
        }

        var normalized = CopyCode.Normalize(code);
        var copyId = normalized is null ? null : await db.Copies.Where(c => c.Code == normalized).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (copyId is null)
        {
            return HandlerResult<LoanDto>.NotFound("No copy has that code.");
        }

        var loans = await db.Loans.AsNoTracking().Where(l => l.CopyId == copyId).WhereOpen().ToListAsync(ct);
        var incoming = loans.FirstOrDefault(l => l.Status == LoanStatus.Mailed && l.BorrowerId == me);
        if (incoming is not null)
        {
            return await ActAsync(me, incoming.Id, LendingActions.Received, body with { Code = normalized }, ct);
        }

        var homecoming = loans.FirstOrDefault(l => l.Status == LoanStatus.ReturnMailed && l.OwnerId == me);
        if (homecoming is not null)
        {
            return await ActAsync(me, homecoming.Id, LendingActions.Returned, body, ct);
        }

        return HandlerResult<LoanDto>.Conflict("This copy isn't on its way to you.");
    }

    public async Task<HandlerResult<LoanDto>> GetAsync(Guid? me, Guid loanId, CancellationToken ct = default)
    {
        if (me is null)
        {
            return HandlerResult<LoanDto>.Unauthorized("Sign in first.");
        }

        var loan = await db.Loans.AsNoTracking().FirstOrDefaultAsync(l => l.Id == loanId, ct);
        if (loan is null || !IsParty(loan, me.Value))
        {
            return HandlerResult<LoanDto>.NotFound("Loan not found.");
        }

        var dtos = await ToDtosAsync(me.Value, [loan], ct);
        return HandlerResult<LoanDto>.Ok(dtos[0]);
    }

    /// <summary>Everything the caller borrows or has asked for, and everything asked of them (as owner or pass-along sender).</summary>
    public async Task<HandlerResult<MyLoansResponse>> MineAsync(Guid? me, bool includeClosed, CancellationToken ct = default)
    {
        if (me is null)
        {
            return HandlerResult<MyLoansResponse>.Unauthorized("Sign in first.");
        }

        var query = db.Loans.AsNoTracking().Where(l => l.BorrowerId == me || l.OwnerId == me || l.SenderId == me);
        if (!includeClosed)
        {
            query = query.WhereOpen();
        }

        var loans = await query.ToListAsync(ct);
        var dtos = await ToDtosAsync(me.Value, loans.OrderByDescending(l => l.UpdatedAt).ToList(), ct);
        return HandlerResult<MyLoansResponse>.Ok(new MyLoansResponse(
            dtos.Where(d => d.Role == "borrower").ToList(),
            dtos.Where(d => d.Role != "borrower").ToList()));
    }

    private async Task<HandlerResult<LoanDto>> SaveAsync(Guid me, CopyAggregate agg, Loan loan, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return HandlerResult<LoanDto>.Conflict("This copy was changed by someone else just now. Reload and try again.");
        }

        var dtos = await ToDtosAsync(me, [loan], ct);
        return HandlerResult<LoanDto>.Ok(dtos[0]);
    }

    private async Task<List<LoanDto>> ToDtosAsync(Guid me, IReadOnlyList<Loan> loans, CancellationToken ct)
    {
        if (loans.Count == 0)
        {
            return [];
        }

        var copyIds = loans.Select(l => l.CopyId).Distinct().ToList();
        var copies = await db.Copies.AsNoTracking().Include(c => c.Book).Where(c => copyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var openByCopy = (await db.Loans.AsNoTracking().Where(l => copyIds.Contains(l.CopyId)).WhereOpen().ToListAsync(ct))
            .GroupBy(l => l.CopyId).ToDictionary(g => g.Key, g => (IReadOnlyCollection<Loan>)g.ToList());
        var names = await LendingQueries.UsersAsync(db, loans.SelectMany(l => new[] { (Guid?)l.OwnerId, l.BorrowerId, l.SenderId }), ct);

        return loans.Select(l =>
        {
            var copy = copies[l.CopyId];
            var open = openByCopy.GetValueOrDefault(l.CopyId) ?? [];
            var role = LendingQueries.Role(l, me);
            var (shipToName, shipTo) = Address(l, me, names);
            int? position = null;
            if (l.Status == LoanStatus.Waitlisted)
            {
                position = open.Where(o => o.Status == LoanStatus.Waitlisted).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id)
                    .TakeWhile(o => o.Id != l.Id).Count() + 1;
            }

            var ownerName = names.Name(l.OwnerId);
            var borrowerName = names.Name(l.BorrowerId);
            var senderName = l.SenderId is null ? null : names.Name(l.SenderId);
            return new LoanDto(l.Id, l.Status, LoanStatusLabels.For(l, role, ownerName, senderName), role, copy.Id, CopyCode.Format(copy.Code),
                copy.Book!.Title, copy.Book.Author, copy.Book.CoverUrl,
                ownerName, borrowerName, senderName, l.IsPassAlong, position,
                l.Message, shipToName, shipTo, LendingActions.For(l, copy, open, me), l.CreatedAt, l.UpdatedAt, l.MailedAt, l.ReceivedAt);
        }).ToList();
    }

    /// <summary>
    /// The privacy rule for mailing addresses (ADR-012): only while a loan is approved and in progress, the owner and the borrower
    /// see each other's address, and a pass-along sender sees the borrower's address until the copy has arrived. Nobody else, never.
    /// </summary>
    private static (string? Name, string? Address) Address(Loan loan, Guid me, NameDirectory names)
    {
        if (!Loan.AddressesShared.Contains(loan.Status))
        {
            return (null, null);
        }

        Guid? other = null;
        if (me == loan.BorrowerId)
        {
            other = loan.OwnerId;
        }
        else if (me == loan.OwnerId || (me == loan.SenderId && loan.Status is LoanStatus.Approved or LoanStatus.Mailed))
        {
            other = loan.BorrowerId;
        }

        return other is not null && names.TryGetUser(other.Value, out var u) ? (names.Name(other), u.MailingAddress) : (null, null);
    }

    private static bool IsParty(Loan loan, Guid me) => loan.BorrowerId == me || loan.OwnerId == me || loan.SenderId == me;

    private async Task<string> NameOfAsync(Guid userId, CancellationToken ct) =>
        (await NameDirectory.LoadAsync(db, [userId], ct)).Name(userId);

    public const string ReaderHasNoAddress = "The reader hasn't added a mailing address yet, so there's nowhere to send the book. Try again once they have.";

    public const string ConfirmEmailFirst = "Confirm your email first: open the link we emailed you when you signed up.";

    /// <summary>Borrowing, reserving and approving need a confirmed email (Louis, Nov 15, 2026; ADR-012).</summary>
    private Task<bool> HasConfirmedEmailAsync(Guid userId, CancellationToken ct) =>
        db.Accounts.AnyAsync(a => a.Id == userId && a.EmailConfirmed, ct);

    private Task<bool> HasAddressAsync(Guid userId, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Id == userId && u.MailingAddress != null && u.MailingAddress != "", ct);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
