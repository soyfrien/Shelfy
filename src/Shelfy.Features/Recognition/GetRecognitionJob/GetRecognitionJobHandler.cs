using Microsoft.EntityFrameworkCore;
using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Recognition.GetRecognitionJob;

public class GetRecognitionJobHandler(ShelfyDbContext db)
{
    // TODO(ADR-006): only the shelf owner may read a job once authentication exists.
    public async Task<HandlerResult<RecognitionJobResponse>> HandleAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await db.RecognitionJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null)
        {
            return HandlerResult<RecognitionJobResponse>.NotFound("Recognition job not found.");
        }

        var spines = await db.DetectedSpines.AsNoTracking()
            .Include(s => s.Book)
            .Where(s => s.RecognitionJobId == jobId)
            .OrderBy(s => s.Position)
            .ToListAsync(ct);

        var spineDtos = spines.Select(s => new DetectedSpineDto(
            s.Position, s.RawText, s.Confidence, s.CandidateTitle, s.CandidateAuthor, s.CandidateIsbn,
            s.Status.ToString(), s.ResolvedIsbn, s.MatchScore, s.MatchSource,
            s.Book is null ? null : ToDto(s.Book))).ToList();

        var books = spines.Where(s => s.Book is not null).Select(s => s.Book!)
            .GroupBy(b => b.Id).Select(g => ToDto(g.First())).ToList();

        return HandlerResult<RecognitionJobResponse>.Ok(new RecognitionJobResponse(
            job.Id, job.ShelfId, job.Status.ToString(), job.Attempts, job.MaxAttempts, Math.Max(0, job.Attempts - 1),
            job.Error, job.BooksAdded, job.CreatedAt, job.StartedAt, job.CompletedAt,
            job.Status == RecognitionJobStatus.Pending ? job.NextAttemptAt : null,
            spineDtos, books));
    }

    private static ResolvedBookDto ToDto(Book b) => new(b.Id, b.Title, b.Author, b.Isbn, b.CoverUrl);
}
