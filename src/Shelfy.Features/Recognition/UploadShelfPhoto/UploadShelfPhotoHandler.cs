using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shelfy.Domain;
using Shelfy.Features.Common;
using Shelfy.Features.Recognition.Storage;
using Shelfy.Features.Recognition.Worker;
using Shelfy.Infrastructure;

namespace Shelfy.Features.Recognition.UploadShelfPhoto;

public class UploadShelfPhotoHandler(
    ShelfyDbContext db,
    IPhotoStorage storage,
    IJobQueue queue,
    IOptions<RecognitionOptions> options,
    TimeProvider clock)
{
    public const long MaxPhotoBytes = 20L * 1024 * 1024;
    public const string TooLargeMessage = "That photo is too large (max 20 MB). Try a smaller photo or a lower camera resolution.";

    public async Task<HandlerResult<UploadShelfPhotoResponse>> HandleAsync(UploadShelfPhotoRequest request, CancellationToken ct = default)
    {
        if (request.ShelfId == Guid.Empty)
        {
            return HandlerResult<UploadShelfPhotoResponse>.Invalid("ShelfId is required.");
        }

        if (request.BatchId is { } batch &&
            (batch == Guid.Empty || request.Parts < 1 || request.Parts > UploadShelfPhotoRequest.MaxParts ||
             request.Part < 1 || request.Part > request.Parts))
        {
            return HandlerResult<UploadShelfPhotoResponse>.Invalid(
                $"With a batchId, 'part' and 'parts' are required: 1 <= part <= parts <= {UploadShelfPhotoRequest.MaxParts}.");
        }

        if (!await db.Shelves.AnyAsync(s => s.Id == request.ShelfId && !s.IsHidden, ct))
        {
            return HandlerResult<UploadShelfPhotoResponse>.NotFound("Shelf not found.");
        }

        if (request.DeclaredLength is > MaxPhotoBytes)
        {
            return HandlerResult<UploadShelfPhotoResponse>.TooLarge(TooLargeMessage);
        }

        // Identify the format from the first bytes, not from the name or declared type.
        var header = new byte[PhotoSignature.HeaderLength];
        var headerLength = await ReadUpToAsync(request.Content, header, ct);
        if (headerLength == 0)
        {
            return HandlerResult<UploadShelfPhotoResponse>.Invalid("The uploaded file is empty.");
        }

        var format = PhotoSignature.Detect(header.AsSpan(0, headerLength));
        if (format == PhotoFormat.Unknown)
        {
            return HandlerResult<UploadShelfPhotoResponse>.UnsupportedMediaType("Only JPEG and PNG photos are accepted.");
        }

        if (!PhotoSignature.ExtensionMatches(request.FileName, format))
        {
            return HandlerResult<UploadShelfPhotoResponse>.UnsupportedMediaType(
                "The file extension does not match the photo content. Only JPEG (.jpg, .jpeg) and PNG (.png) photos are accepted.");
        }

        var jobId = Guid.NewGuid();
        var contentType = PhotoSignature.ContentType(format);
        var key = $"shelves/{request.ShelfId:N}/{jobId:N}.{PhotoSignature.Extension(format)}";

        using var metered = new MeteredHashingStream(request.Content, header[..headerLength], MaxPhotoBytes);
        try
        {
            await storage.SaveAsync(key, metered, contentType, ct);
        }
        catch (PhotoTooLargeException)
        {
            await storage.DeleteAsync(key, CancellationToken.None);
            return HandlerResult<UploadShelfPhotoResponse>.TooLarge(TooLargeMessage);
        }

        var hash = metered.Sha256Hex;

        // Too small for OCR: say so now instead of failing later with the OCR service's raw error.
        if (await DimensionsAsync(key, format, ct) is { } size && Math.Min(size.Width, size.Height) < PhotoDimensions.MinSidePixels)
        {
            await storage.DeleteAsync(key, CancellationToken.None);
            return HandlerResult<UploadShelfPhotoResponse>.Invalid(TooSmallMessage(size.Width, size.Height));
        }

        try
        {
            // A retried upload of the same photo must not create a second job.
            // Within a batch the same photo is a duplicate only inside that batch (a failed part is retried as a new job).
            var batchId = request.BatchId;
            var existing = await db.RecognitionJobs.AsNoTracking()
                .Where(j => j.ShelfId == request.ShelfId && j.PhotoSha256 == hash && j.Status != RecognitionJobStatus.Failed)
                .Where(j => batchId == null || j.BatchId == batchId)
                .OrderBy(j => j.CreatedAt)
                .Select(j => new { j.Id, j.Status })
                .FirstOrDefaultAsync(ct);
            if (existing is not null)
            {
                await storage.DeleteAsync(key, CancellationToken.None);
                return HandlerResult<UploadShelfPhotoResponse>.Ok(Response(existing.Id, existing.Status, duplicate: true));
            }

            var job = RecognitionJob.CreatePending(request.ShelfId, key, contentType, metered.BytesRead, hash,
                Math.Max(1, options.Value.MaxAttempts), clock.GetUtcNow().UtcDateTime,
                request.BatchId, request.Part, request.Parts);
            job.Id = jobId;
            db.RecognitionJobs.Add(job);
            await db.SaveChangesAsync(ct);
            await queue.EnqueueAsync(job.Id, ct);

            return HandlerResult<UploadShelfPhotoResponse>.Ok(Response(job.Id, job.Status, duplicate: false));
        }
        catch
        {
            await storage.DeleteAsync(key, CancellationToken.None);
            throw;
        }
    }

    public static string TooSmallMessage(int width, int height) =>
        $"That photo is too small ({width} x {height} pixels). Use a photo at least {PhotoDimensions.MinSidePixels} pixels on each side, ideally straight from your camera.";

    private async Task<(int Width, int Height)?> DimensionsAsync(string key, PhotoFormat format, CancellationToken ct)
    {
        try
        {
            await using var stored = await storage.OpenReadAsync(key, ct);
            return await PhotoDimensions.ReadAsync(stored, format, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // can't tell: let it through as before
        }
    }

    public static string StatusUrl(Guid jobId) => $"/api/recognition-jobs/{jobId}";

    private static UploadShelfPhotoResponse Response(Guid id, RecognitionJobStatus status, bool duplicate) =>
        new(id, status.ToString(), StatusUrl(id), duplicate);

    private static async Task<int> ReadUpToAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
