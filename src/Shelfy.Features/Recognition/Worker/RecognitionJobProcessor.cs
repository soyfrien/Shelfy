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

public record ProcessingSummary(int SpinesDetected, int SpinesResolved, int BooksAdded, int DuplicatesDropped = 0);

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
        if (shelf.IsHidden)
        {
            // The owner cleared the shelf while the photo was waiting: do not read it, spend no OCR call, add nothing.
            throw new RecognitionException("The shelf was cleared before this photo was read.", isRetryable: false);
        }

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

        // Spines already found by the other photos (tiles) of the same batch: the overlap zones show the same spines twice.
        var peers = job.BatchId is { } batchId
            ? await db.DetectedSpines.Include(sp => sp.Book)
                .Where(sp => sp.RecognitionJobId != jobId &&
                             db.RecognitionJobs.Any(j => j.Id == sp.RecognitionJobId && j.ShelfId == job.ShelfId && j.BatchId == batchId))
                .ToListAsync(ct)
            : new List<DetectedSpine>();
        var tiled = job.TileCount > 1;
        var dropped = 0;

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

            // In a tile, a read that is only a person's name is most likely the author line of a spine whose title was cut off
            // by the tile edge (the whole spine is in a neighbouring tile). Looking it up as a title could add a wrong book.
            var authorOnlyFragment = tiled && candidate.Author is null && candidate.Isbn is null &&
                                     TextNormalizer.LooksLikeAuthorName(candidate.Title);
            var match = authorOnlyFragment || (candidate.Title.Length < 2 && candidate.Isbn is null)
                ? null
                : await ResolveAsync(candidate, opts.MinMatchScore, ct);
            if (match is null || match.Score < opts.MinMatchScore || Isbn.Normalize(match.Isbn) is not { } isbn)
            {
                // Unresolved: in a tiled photo, keep it only if it is not noise, not an edge fragment of a spine
                // another tile read better, and not a repeat of one it read just as well.
                if (tiled && SpineDeduper.IsJunkFragment(candidate.RawText, candidate.Confidence))
                {
                    dropped++;
                    continue;
                }

                if (!KeepUnresolved(candidate, peers, out var superseded))
                {
                    dropped++;
                    continue;
                }

                foreach (var old in superseded)
                {
                    db.DetectedSpines.Remove(old);
                    peers.Remove(old);
                    dropped++;
                }

                db.DetectedSpines.Add(spine);
                continue;
            }

            db.DetectedSpines.Add(spine);

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

            // An unresolved read of the same spine from a neighbouring tile (a fragment) is now explained by this book.
            foreach (var old in peers.Where(p => p.Status == SpineResolutionStatus.Unresolved &&
                                                  (SpineDeduper.IsSameSpine(p.RawText, candidate.RawText) ||
                                                   SpineDeduper.IsSameSpine(p.RawText, $"{book.Title} {book.Author}") ||
                                                   SpineDeduper.IsSameSpine(p.RawText, book.Title))).ToList())
            {
                db.DetectedSpines.Remove(old);
                peers.Remove(old);
                dropped++;
            }

            // Shelf.AddBook keeps the shelf free of duplicates and adds the book as Private.
            var shelfBook = shelf.AddBook(book.Id);
            if (shelfBook is not null)
            {
                added++;
            }
        }

        job.BooksAdded = added;
        job.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // If the shelf was deleted while this photo was being read, nothing was saved: fail quietly, without a retry.
            if (await ShelfGoneAsync(job.ShelfId, jobId))
            {
                throw new RecognitionException("The shelf was cleared while this photo was being read.", isRetryable: false);
            }

            throw;
        }

        if (dropped > 0)
        {
            logger.LogInformation("Job {JobId}: dropped {Dropped} duplicate or junk spine reads from overlapping tiles", jobId, dropped);
        }

        return new ProcessingSummary(candidates.Count, resolved, added, dropped);
    }

    /// <summary>True when the job's shelf was hard-deleted (the job went with it) or hidden since the job was loaded.</summary>
    private async Task<bool> ShelfGoneAsync(Guid shelfId, Guid jobId)
    {
        db.ChangeTracker.Clear();
        return !await db.RecognitionJobs.AsNoTracking().AnyAsync(j => j.Id == jobId, CancellationToken.None) ||
               !await db.Shelves.AsNoTracking().AnyAsync(s => s.Id == shelfId && !s.IsHidden, CancellationToken.None);
    }

    /// <summary>
    /// Decides whether an unresolved read is a new spine. It is a duplicate when a peer from the same batch (another tile)
    /// reads the same spine at least as well, or a resolved peer's text or book explains it. When the new read is better
    /// than unresolved peers of the same spine, those are returned in <paramref name="superseded"/> to be replaced.
    /// </summary>
    public static bool KeepUnresolved(SpineCandidate candidate, List<DetectedSpine> peers, out List<DetectedSpine> superseded)
    {
        superseded = new List<DetectedSpine>();
        var quality = SpineDeduper.Quality(candidate.RawText, candidate.Confidence);
        foreach (var peer in peers)
        {
            var same = SpineDeduper.IsSameSpine(peer.RawText, candidate.RawText) ||
                       (peer.Book is { } b && (SpineDeduper.IsSameSpine(candidate.RawText, b.Title) ||
                                               SpineDeduper.IsSameSpine(candidate.RawText, $"{b.Title} {b.Author}")));
            if (!same)
            {
                continue;
            }

            if (peer.Status == SpineResolutionStatus.Resolved ||
                SpineDeduper.Quality(peer.RawText, peer.Confidence) >= quality)
            {
                superseded.Clear();
                return false;
            }

            superseded.Add(peer);
        }

        return true;
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
