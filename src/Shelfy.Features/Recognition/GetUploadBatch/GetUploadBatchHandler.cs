using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Features.Recognition.GetRecognitionJob;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Recognition.GetUploadBatch;

public class GetUploadBatchHandler(ShelfyDbContext db)
{
    // TODO(ADR-006): only the shelf owner may read a batch once authentication exists.
    public async Task<HandlerResult<UploadBatchResponse>> HandleAsync(Guid shelfId, Guid batchId, CancellationToken ct = default)
    {
        // A hidden shelf is gone as far as the public API is concerned (ADR-010).
        if (!await db.Shelves.AsNoTracking().AnyAsync(s => s.Id == shelfId && !s.IsHidden, ct))
        {
            return HandlerResult<UploadBatchResponse>.NotFound("Batch not found.");
        }

        var allJobs = await db.RecognitionJobs.AsNoTracking()
            .Where(j => j.ShelfId == shelfId && j.BatchId == batchId)
            .ToListAsync(ct);
        if (allJobs.Count == 0)
        {
            return HandlerResult<UploadBatchResponse>.NotFound("Batch not found.");
        }

        // A failed part that was uploaded again has two jobs: the newest one counts.
        var jobs = allJobs.GroupBy(j => j.TileIndex)
            .Select(g => g.OrderByDescending(j => j.CreatedAt).First())
            .OrderBy(j => j.TileIndex)
            .ToList();

        var ids = jobs.Select(j => j.Id).ToList();
        var spines = await db.DetectedSpines.AsNoTracking()
            .Include(s => s.Book)
            .Where(s => ids.Contains(s.RecognitionJobId))
            .ToListAsync(ct);

        var partOf = jobs.ToDictionary(j => j.Id, j => j.TileIndex);
        var ordered = spines.OrderBy(s => partOf[s.RecognitionJobId]).ThenBy(s => s.Position).ToList();

        // Resolved: one entry per book (best score). Unresolved: all that survived de-duplication when the tiles were processed.
        var resolved = ordered.Where(s => s.Book is not null)
            .GroupBy(s => s.Book!.Id)
            .Select(g => g.OrderByDescending(s => s.MatchScore ?? 0).First());
        var unresolved = ordered.Where(s => s.Book is null);
        var keep = resolved.Concat(unresolved)
            .OrderBy(s => partOf[s.RecognitionJobId]).ThenBy(s => s.Position).ToList();

        var spineDtos = keep.Select(s => new DetectedSpineDto(
            s.Position, s.RawText, s.Confidence, s.CandidateTitle, s.CandidateAuthor, s.CandidateIsbn,
            s.Status.ToString(), s.ResolvedIsbn, s.MatchScore, s.MatchSource,
            s.Book is null ? null : new ResolvedBookDto(s.Book.Id, s.Book.Title, s.Book.Author, s.Book.Isbn, s.Book.CoverUrl))).ToList();
        var books = keep.Where(s => s.Book is not null).Select(s => s.Book!)
            .Select(b => new ResolvedBookDto(b.Id, b.Title, b.Author, b.Isbn, b.CoverUrl)).ToList();

        var spinesPerJob = spines.GroupBy(s => s.RecognitionJobId).ToDictionary(g => g.Key, g => g.Count());
        var parts = Math.Max(jobs.Max(j => j.TileCount), jobs.Count);
        var count = (RecognitionJobStatus st) => jobs.Count(j => j.Status == st);
        var pending = count(RecognitionJobStatus.Pending);
        var processing = count(RecognitionJobStatus.Processing);
        var completed = count(RecognitionJobStatus.Completed);
        var failed = count(RecognitionJobStatus.Failed);

        return HandlerResult<UploadBatchResponse>.Ok(new UploadBatchResponse(
            batchId, shelfId, parts, jobs.Count, pending, processing, completed, failed,
            Done: jobs.Count >= parts && pending == 0 && processing == 0,
            BooksAdded: jobs.Sum(j => j.BooksAdded),
            Jobs: jobs.Select(j => new BatchPartDto(j.TileIndex, j.Id, j.Status.ToString(), j.Attempts, j.MaxAttempts, j.Error,
                j.Status == RecognitionJobStatus.Pending ? j.NextAttemptAt : null,
                spinesPerJob.GetValueOrDefault(j.Id))).ToList(),
            Spines: spineDtos,
            Books: books));
    }
}
