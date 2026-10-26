using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Features.Recognition.Parsing;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Recognition.GetShelfUnmatched;

/// <param name="Id">The spine to fix (tap to fix, ADR-011). When the same text was read in several photos, one of them.</param>
/// <param name="LowQuality">Set only when low-quality reads were asked for: this read would normally be hidden.</param>
/// <param name="Reason">Why it is low quality: short, punctuation, filler, unsure or duplicate.</param>
public record UnmatchedSpineDto(Guid Id, string Text, string CandidateTitle, bool LowQuality = false, string? Reason = null);

/// <param name="Spines">What to list. Low-quality reads are left out unless <c>includeHidden</c> was asked for.</param>
/// <param name="HiddenLowQuality">How many distinct reads were judged low quality (too short, unsure, a repeat of a better read).</param>
public record ShelfUnmatchedResponse(Guid ShelfId, IReadOnlyList<UnmatchedSpineDto> Spines, int HiddenLowQuality = 0);

/// <summary>
/// The spines that were read from a shelf's photos but not matched to a book, across all of its photos. The web UI
/// searches this list too, so a book that was not recognized can still be found by its spine text (also after a reload,
/// when the batch that produced it is no longer known to the browser). Spines the user dismissed are not listed, and very
/// poor reads are left out (<see cref="SpineNoiseFilter"/>) but counted, so the UI can say "N hidden" and show them on request.
/// </summary>
public class GetShelfUnmatchedHandler(ShelfyDbContext db)
{
    public const int MaxSpines = 1000;

    // TODO(ADR-006): only the shelf owner may read this once authentication exists.
    public async Task<HandlerResult<ShelfUnmatchedResponse>> HandleAsync(Guid shelfId, bool includeHidden = false, CancellationToken ct = default)
    {
        // A hidden shelf is gone as far as the public API is concerned (ADR-010).
        if (!await db.Shelves.AsNoTracking().AnyAsync(s => s.Id == shelfId && !s.IsHidden, ct))
        {
            return HandlerResult<ShelfUnmatchedResponse>.NotFound("Shelf not found.");
        }

        var rows = await (
                from s in db.DetectedSpines.AsNoTracking()
                join j in db.RecognitionJobs.AsNoTracking() on s.RecognitionJobId equals j.Id
                where j.ShelfId == shelfId && j.Status == RecognitionJobStatus.Completed && s.BookId == null && !s.Dismissed
                orderby j.CreatedAt, j.TileIndex, s.Position
                select new { s.Id, s.RawText, s.CandidateTitle, s.Confidence })
            .Take(MaxSpines * 3)
            .ToListAsync(ct);

        var (spines, hidden) = Build(rows.Select(r => new Read(r.Id, r.RawText, r.CandidateTitle, r.Confidence)).ToList(), includeHidden);
        return HandlerResult<ShelfUnmatchedResponse>.Ok(new ShelfUnmatchedResponse(shelfId, spines, hidden));
    }

    public record Read(Guid Id, string? RawText, string? CandidateTitle, double? Confidence);

    /// <summary>Pure: de-duplicates, applies the noise filter, and returns the list plus the number of hidden reads.</summary>
    public static (IReadOnlyList<UnmatchedSpineDto> Spines, int Hidden) Build(IReadOnlyList<Read> rows, bool includeHidden)
    {
        // The same text seen in two photos, or in the overlap of two tiles, is listed once (case and punctuation ignored).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<(Read Read, string Text, string? Reason)>();
        foreach (var r in rows)
        {
            var text = (r.RawText ?? "").Trim();
            if (text.Length == 0 || !seen.Add(SpineText.Key(text)))
            {
                continue;
            }

            entries.Add((r, text, SpineNoiseFilter.Reason(text, r.Confidence)));
        }

        // A read that is a cut-off or noisier repeat of another listed read ("WILLIAM | SAFIRE" next to
        // "William Safire In Love with Norma Loqu") is hidden as a duplicate; the better read stays.
        var tokens = entries.Select(e => SpineDeduper.Tokens(e.Text)).ToList();
        var quality = entries.Select(e => SpineDeduper.Quality(e.Text, e.Read.Confidence)).ToList();
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Reason is not null)
            {
                continue;
            }

            for (var j = 0; j < entries.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }

                if (entries[j].Reason is not null || !SpineDeduper.IsSameSpine(tokens[i], tokens[j]))
                {
                    continue;
                }

                if (quality[j] > quality[i] || (quality[j] == quality[i] && j < i))
                {
                    entries[i] = entries[i] with { Reason = SpineNoiseFilter.Duplicate };
                    break;
                }
            }
        }

        var hidden = entries.Count(e => e.Reason is not null);
        var listed = new List<UnmatchedSpineDto>();
        foreach (var e in entries)
        {
            if (e.Reason is not null && !includeHidden)
            {
                continue;
            }

            listed.Add(new UnmatchedSpineDto(e.Read.Id, e.Text, e.Read.CandidateTitle ?? "", e.Reason is not null, e.Reason));
            if (listed.Count >= MaxSpines)
            {
                break;
            }
        }

        return (listed, hidden);
    }
}
