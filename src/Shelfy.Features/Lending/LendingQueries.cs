using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Lending;

/// <summary>A copy with everything the state machine needs, tracked for update.</summary>
public sealed record CopyAggregate(Copy Copy, List<Loan> Loans, List<CopyMovement> Movements);

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

    public static async Task<Dictionary<Guid, User>> UsersAsync(ShelfyDbContext db, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var set = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => set.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
    }

    public static string Name(this Dictionary<Guid, User> users, Guid? id) =>
        id is not null && users.TryGetValue(id.Value, out var u) && !string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName : "Someone";

    public static CopyViewerDto Viewer(Copy copy, IReadOnlyCollection<Loan> loans, Guid? me)
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
            MyLoan: mine is null ? null : new LoanSummaryDto(mine.Id, mine.Status, LendingActions.For(mine, copy, loans, me)));
    }
}
