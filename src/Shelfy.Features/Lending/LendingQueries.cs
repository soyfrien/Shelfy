using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Lending;

/// <summary>A copy with everything the state machine needs, tracked for update.</summary>
public sealed record CopyAggregate(Copy Copy, List<Loan> Loans, List<CopyMovement> Movements);

/// <summary>Display names for a set of users, never including a full email address.</summary>
internal sealed class NameDirectory
{
    public NameDirectory(Dictionary<Guid, User> users, Dictionary<Guid, ShelfyAccount> accounts)
    {
        Users = users;
        Accounts = accounts;
    }

    public Dictionary<Guid, User> Users { get; }
    public Dictionary<Guid, ShelfyAccount> Accounts { get; }

    public bool TryGetUser(Guid id, out User user) => Users.TryGetValue(id, out user!);

    public string Name(Guid? id)
    {
        if (id is null)
        {
            return PublicUserName.Fallback;
        }

        Users.TryGetValue(id.Value, out var user);
        Accounts.TryGetValue(id.Value, out var account);
        return PublicUserName.Of(user, account);
    }

    public static async Task<NameDirectory> LoadAsync(ShelfyDbContext db, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var set = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => set.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var accounts = await db.Accounts.AsNoTracking().Where(a => set.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        return new NameDirectory(users, accounts);
    }
}

internal static class LendingQueries
{
    /// <summary>Loans that are not finished (spelled out so every provider translates it).</summary>
    public static IQueryable<Loan> WhereOpen(this IQueryable<Loan> loans) => loans.Where(l =>
        l.Status != LoanStatus.Declined && l.Status != LoanStatus.Cancelled && l.Status != LoanStatus.Returned && l.Status != LoanStatus.PassedOn);

    public static async Task<CopyAggregate?> LoadAsync(ShelfyDbContext db, Guid copyId, CancellationToken ct)
    {
        var copy = await db.Copies.Include(c => c.Book).FirstOrDefaultAsync(c => c.Id == copyId, ct);
        if (copy is null)
        {
            return null;
        }

        var loans = await db.Loans.Where(l => l.CopyId == copyId).ToListAsync(ct);
        var movements = await db.CopyMovements.Where(m => m.CopyId == copyId).ToListAsync(ct);
        return new CopyAggregate(copy, loans, movements);
    }

    public static Task<NameDirectory> UsersAsync(ShelfyDbContext db, IEnumerable<Guid?> ids, CancellationToken ct) =>
        NameDirectory.LoadAsync(db, ids, ct);

    public static string Role(Loan loan, Guid me) =>
        loan.BorrowerId == me ? "borrower" : loan.OwnerId == me ? "owner" : "sender";

    public static CopyViewerDto Viewer(Copy copy, IReadOnlyCollection<Loan> loans, Guid? me, NameDirectory names)
    {
        if (me is null)
        {
            return new CopyViewerDto(false, false, false, false, false, null);
        }

        var isOwner = copy.OwnerId == me;
        var mine = loans.Where(l => l.BorrowerId == me && l.IsOpen).OrderByDescending(l => l.CreatedAt).FirstOrDefault();
        var holder = copy.CurrentHolderId == me && copy.Status != CopyStatus.InTransit;
        var free = !isOwner && mine is null && !holder;
        return new CopyViewerDto(
            true, isOwner, holder,
            CanBorrow: free && copy.Status == CopyStatus.Available,
            CanReserve: free && copy.Status is CopyStatus.Reserved or CopyStatus.OnLoan or CopyStatus.InTransit,
            MyLoan: mine is null ? null : new LoanSummaryDto(
                mine.Id, mine.Status,
                LoanStatusLabels.For(mine, "borrower", names.Name(mine.OwnerId), mine.SenderId is null ? null : names.Name(mine.SenderId)),
                LendingActions.For(mine, copy, loans, me)),
            AwaitingReturn: isOwner && loans.Any(l => l.Status == LoanStatus.ReturnMailed));
    }

    /// <summary>Rewrite conversational domain errors that said "the owner" to use the person's name.</summary>
    public static LendingException WithOwnerName(this LendingException ex, string ownerName)
    {
        const string owner = "The owner";
        if (string.IsNullOrWhiteSpace(ownerName) || !ex.Message.StartsWith(owner, StringComparison.Ordinal))
        {
            return ex;
        }

        return new LendingException(ex.Error, ownerName + ex.Message[owner.Length..]);
    }
}
