using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;
using Shelfy.Features.Recognition.Parsing;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Recognition.FixSpine;

internal static class SpineFixSupport
{
    /// <summary>The spine, only when it belongs to a visible shelf's photo. Hidden or unknown shelves, and spines of other shelves, are "not found".</summary>
    public static async Task<DetectedSpine?> FindSpineAsync(ShelfyDbContext db, Guid shelfId, Guid spineId, bool track, CancellationToken ct)
    {
        if (!await db.Shelves.AsNoTracking().AnyAsync(s => s.Id == shelfId && !s.IsHidden, ct))
        {
            return null;
        }

        var spines = track ? db.DetectedSpines : db.DetectedSpines.AsNoTracking();
        // No join with an AsNoTracking source: that would make the whole query untracked.
        return await spines.Where(s => s.Id == spineId && db.RecognitionJobs.Any(j => j.Id == s.RecognitionJobId && j.ShelfId == shelfId))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Every unresolved read on the shelf with the same text as <paramref name="spine"/> (the list shows a text once, however
    /// many photos or tiles read it), the spine itself included. Tracked, so the caller can change them.
    /// </summary>
    public static async Task<List<DetectedSpine>> SameTextUnresolvedAsync(ShelfyDbContext db, Guid shelfId, DetectedSpine spine, CancellationToken ct)
    {
        var key = SpineText.Key(spine.RawText);
        var candidates = await db.DetectedSpines
            .Where(s => s.BookId == null && db.RecognitionJobs.Any(j => j.Id == s.RecognitionJobId && j.ShelfId == shelfId))
            .ToListAsync(ct);
        var same = candidates.Where(s => s.Id == spine.Id || SpineText.Key(s.RawText) == key).ToList();
        if (same.All(s => s.Id != spine.Id) && spine.BookId is null)
        {
            same.Add(spine);
        }

        return same;
    }
}
