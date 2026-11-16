using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shelfy.Features.Common;
using Shelfy.Features.Recognition.Storage;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Shelves.DeleteShelf;

/// <summary>
/// Hard "clear shelf" (ADR-010): removes the shelf with its book links, recognition jobs, detected spines and stored photos.
/// The shared Books catalog and the (anonymous) user stay. Idempotent: deleting an unknown or already deleted shelf succeeds.
/// A job being processed at that moment fails quietly (the processor notices the shelf is gone) and is not retried.
/// </summary>
public class DeleteShelfHandler(ShelfyDbContext db, IPhotoStorage storage, ILogger<DeleteShelfHandler> logger)
{
    // Who may call this is enforced by ShelfOwnerFilter on the endpoint: only the owner for account shelves, the link for guest shelves.
    public async Task<HandlerResult<DeleteShelfResponse>> HandleAsync(Guid shelfId, CancellationToken ct = default)
    {
        var shelf = await db.Shelves.Include(s => s.Books).FirstOrDefaultAsync(s => s.Id == shelfId, ct);
        var jobs = await db.RecognitionJobs.Where(j => j.ShelfId == shelfId).ToListAsync(ct);
        var jobIds = jobs.Select(j => j.Id).ToList();
        var spines = jobIds.Count == 0
            ? new List<Domain.DetectedSpine>()
            : await db.DetectedSpines.Where(s => jobIds.Contains(s.RecognitionJobId)).ToListAsync(ct);

        // Rows first (one SaveChanges = one transaction, explicit so it does not depend on cascade settings), files after:
        // a file left behind by a failure is only disk space, a missing file for a live row would be an error.
        db.DetectedSpines.RemoveRange(spines);
        db.RecognitionJobs.RemoveRange(jobs);
        if (shelf is not null)
        {
            db.RemoveRange(shelf.Books);
            db.Shelves.Remove(shelf);
        }

        await db.SaveChangesAsync(ct);

        var photosRemoved = 0;
        foreach (var key in jobs.Select(j => j.PhotoKey).Distinct())
        {
            try
            {
                await storage.DeleteAsync(key, CancellationToken.None);
                photosRemoved++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete photo {Key} of deleted shelf {ShelfId}", key, shelfId);
            }
        }

        try
        {
            await storage.DeleteFolderAsync($"shelves/{shelfId:N}", CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete the photo folder of deleted shelf {ShelfId}", shelfId);
        }

        logger.LogInformation("Shelf {ShelfId} deleted: {Books} book links, {Jobs} jobs, {Spines} spines, {Photos} photos",
            shelfId, shelf?.Books.Count ?? 0, jobs.Count, spines.Count, photosRemoved);
        return HandlerResult<DeleteShelfResponse>.Ok(new DeleteShelfResponse(shelfId));
    }
}

public record DeleteShelfResponse(Guid ShelfId);
