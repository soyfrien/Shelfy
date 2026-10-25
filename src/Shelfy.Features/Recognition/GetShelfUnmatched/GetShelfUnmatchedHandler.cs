using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Recognition.GetShelfUnmatched;

public record UnmatchedSpineDto(string Text, string CandidateTitle);

public record ShelfUnmatchedResponse(Guid ShelfId, IReadOnlyList<UnmatchedSpineDto> Spines);

/// <summary>
/// The spines that were read from a shelf's photos but not matched to a book, across all of its photos. The web UI
/// searches this list too, so a book that was not recognized can still be found by its spine text (also after a reload,
/// when the batch that produced it is no longer known to the browser).
/// </summary>
public class GetShelfUnmatchedHandler(ShelfyDbContext db)
{
    public const int MaxSpines = 1000;

    // TODO(ADR-006): only the shelf owner may read this once authentication exists.
    public async Task<HandlerResult<ShelfUnmatchedResponse>> HandleAsync(Guid shelfId, CancellationToken ct = default)
    {
        // A hidden shelf is gone as far as the public API is concerned (ADR-010).
        if (!await db.Shelves.AsNoTracking().AnyAsync(s => s.Id == shelfId && !s.IsHidden, ct))
        {
            return HandlerResult<ShelfUnmatchedResponse>.NotFound("Shelf not found.");
        }

        var rows = await (
                from s in db.DetectedSpines.AsNoTracking()
                join j in db.RecognitionJobs.AsNoTracking() on s.RecognitionJobId equals j.Id
                where j.ShelfId == shelfId && j.Status == RecognitionJobStatus.Completed && s.BookId == null
                orderby j.CreatedAt, j.TileIndex, s.Position
                select new { s.RawText, s.CandidateTitle })
            .Take(MaxSpines * 3)
            .ToListAsync(ct);

        // The same text seen in two photos, or in the overlap of two tiles, is listed once.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var spines = new List<UnmatchedSpineDto>();
        foreach (var r in rows)
        {
            var text = (r.RawText ?? "").Trim();
            if (text.Length == 0 || !seen.Add(text))
            {
                continue;
            }

            spines.Add(new UnmatchedSpineDto(text, r.CandidateTitle ?? ""));
            if (spines.Count >= MaxSpines)
            {
                break;
            }
        }

        return HandlerResult<ShelfUnmatchedResponse>.Ok(new ShelfUnmatchedResponse(shelfId, spines));
    }
}
