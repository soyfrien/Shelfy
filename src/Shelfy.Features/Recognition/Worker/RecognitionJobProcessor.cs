using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shelfy.Domain;
using Shelfy.Features.Recognition.Ocr;
using Shelfy.Features.Recognition.Parsing;
using Shelfy.Features.Recognition.Resolution;
using Shelfy.Features.Recognition.Storage;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Recognition.Worker;

public record ProcessingSummary(int SpinesDetected, int SpinesResolved, int BooksAdded);

/// <summary>
/// Runs the pipeline for one claimed job: photo -> OCR -> spine parsing -> book resolution -> shelf.
/// It does not change the job status (the queue does); it writes spines, books and shelf links in a single SaveChanges,
/// so a retry after a crash starts from a clean slate: old spines are replaced and books are never added twice.
/// Processes spines one at a time and streams the photo, to stay small on a small VM.
/// </summary>
public class RecognitionJobProcessor(
    ShelfyDbContext db,
    IPhotoStorage storage,
    ITextRecognizer recognizer,
    IBookResolver resolver,
    IOptions<RecognitionOptions> options,
    TimeProvider clock,
    ILogger<RecognitionJobProcessor> logger)
{
    public async Task<ProcessingSummary> ProcessAsync(Guid jobId, CancellationToken ct = default)
    {
        var opts = options.Value;
        var job = await db.RecognitionJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new RecognitionException("Recognition job no longer exists.", isRetryable: false);
        var shelf = await db.Shelves.Include(s => s.Books).FirstOrDefaultAsync(s => s.Id == job.ShelfId, ct)
            ?? throw new RecognitionException("The shelf for this job no longer exists.", isRetryable: false);

        OcrResult ocr;
        try
        {
            await using var photo = await storage.OpenReadAsync(job.PhotoKey, ct);
            ocr = await recognizer.RecognizeAsync(photo, job.ContentType, ct);
        }
        catch (FileNotFoundException ex)
        {
            throw new RecognitionException("The stored photo is missing.", isRetryable: false, ex);
        }

        var candidates = SpineTextParser.Parse(ocr, opts.MaxSpinesPerPhoto);
        logger.LogInformation("Job {JobId}: OCR returned {Lines} lines, parsed {Spines} spines", jobId, ocr.Lines.Count, candidates.Count);

        // Idempotent on retry: replace any spines from an earlier, interrupted attempt.
        var previous = await db.DetectedSpines.Where(s => s.RecognitionJobId == jobId).ToListAsync(ct);
        db.DetectedSpines.RemoveRange(previous);

        var booksByIsbn = new Dictionary<string, Book>();
        var resolved = 0;
        var added = 0;

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var spine = new DetectedSpine
            {
                Id = Guid.NewGuid(),
                RecognitionJobId = jobId,
                Position = candidate.Position,
                RawText = candidate.RawText,
                Confidence = candidate.Confidence,
                CandidateTitle = candidate.Title,
                CandidateAuthor = candidate.Author,
                CandidateIsbn = candidate.Isbn
            };
            db.DetectedSpines.Add(spine);

            if (candidate.Title.Length < 2 && candidate.Isbn is null)
            {
                continue;
            }

            var match = await ResolveAsync(candidate, opts.MinMatchScore, ct);
            if (match is null || match.Score < opts.MinMatchScore || Isbn.Normalize(match.Isbn) is not { } isbn)
            {
                continue;
            }

            if (!booksByIsbn.TryGetValue(isbn, out var book))
            {
                book = await db.Books.FirstOrDefaultAsync(b => b.Isbn == isbn, ct);
                if (book is null)
                {
                    book = new Book
                    {
                        Id = Guid.NewGuid(),
                        Isbn = isbn,
                        Title = Truncate(match.Title, 500),
                        Author = Truncate(string.IsNullOrWhiteSpace(match.Author) ? "Unknown" : match.Author, 300),
                        CoverUrl = string.IsNullOrWhiteSpace(match.CoverUrl) ? null : Truncate(match.CoverUrl, 2000)
                    };
                    db.Books.Add(book);
                }

                booksByIsbn[isbn] = book;
            }

            spine.MarkResolved(book, match.Score, match.Source);
            resolved++;

            // Shelf.AddBook keeps the shelf free of duplicates and adds the book as Private.
            var shelfBook = shelf.AddBook(book.Id);
            if (shelfBook is not null)
            {
                added++;
            }
        }

        job.BooksAdded = added;
        job.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        return new ProcessingSummary(candidates.Count, resolved, added);
    }

    /// <summary>
    /// Looks the spine up with title+author. When that finds nothing acceptable and an author was guessed, tries the
    /// title alone: the guessed "author" may belong to a neighbouring book.
    /// </summary>
    private async Task<BookMatch?> ResolveAsync(SpineCandidate candidate, double minScore, CancellationToken ct)
    {
        var match = await resolver.ResolveAsync(new BookQuery(candidate.Title, candidate.Author, candidate.Isbn), ct);
        if ((match is null || match.Score < minScore) && candidate.Author is not null && candidate.Title.Length >= 2)
        {
            match = await resolver.ResolveAsync(new BookQuery(candidate.Title, null, candidate.Isbn), ct) ?? match;
        }

        return match;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
