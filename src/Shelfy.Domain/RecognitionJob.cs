namespace Shelfy.Domain;

/// <summary>
/// One uploaded shelf photo waiting to be (or already) recognized. The photo itself lives in photo storage;
/// the job row is the durable queue entry and the user-visible status (ADR-008).
/// All timestamps are UTC.
/// </summary>
public class RecognitionJob
{
    public Guid Id { get; set; }
    public Guid ShelfId { get; set; }

    /// <summary>Opaque key understood by the configured photo storage.</summary>
    public string PhotoKey { get; set; } = "";

    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }

    /// <summary>Lowercase hex SHA-256 of the photo. Lets a retried upload reuse the existing job.</summary>
    public string PhotoSha256 { get; set; } = "";

    public RecognitionJobStatus Status { get; set; } = RecognitionJobStatus.Pending;

    /// <summary>How many times processing has been started (claimed).</summary>
    public int Attempts { get; set; }

    public int MaxAttempts { get; set; } = 4;

    /// <summary>Earliest time a worker may claim the job (used for retry backoff).</summary>
    public DateTime NextAttemptAt { get; set; }

    /// <summary>While Processing: when the claim expires and another worker may take over.</summary>
    public DateTime? LockedUntil { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Last failure message, kept while retrying and after the final failure.</summary>
    public string? Error { get; set; }

    /// <summary>How many books this job added to the shelf (already-present books are not counted).</summary>
    public int BooksAdded { get; set; }

    /// <summary>
    /// Photos uploaded together (the tiles of one wide shelf photo, or a single photo) share a batch id chosen by the client.
    /// Null for jobs uploaded without one (older clients). Used for progress ("part k of n") and for de-duplicating spines
    /// seen in the overlap of neighbouring tiles.
    /// </summary>
    public Guid? BatchId { get; set; }

    /// <summary>1-based position of this photo within its batch; 0 when there is no batch.</summary>
    public int TileIndex { get; set; }

    /// <summary>How many photos the client announced for the batch; 0 when there is no batch.</summary>
    public int TileCount { get; set; }

    public ICollection<DetectedSpine> Spines { get; set; } = new List<DetectedSpine>();

    public static RecognitionJob CreatePending(Guid shelfId, string photoKey, string contentType, long sizeBytes,
        string photoSha256, int maxAttempts, DateTime nowUtc, Guid? batchId = null, int tileIndex = 0, int tileCount = 0) => new()
    {
        Id = Guid.NewGuid(),
        ShelfId = shelfId,
        PhotoKey = photoKey,
        ContentType = contentType,
        SizeBytes = sizeBytes,
        PhotoSha256 = photoSha256,
        MaxAttempts = maxAttempts,
        Status = RecognitionJobStatus.Pending,
        NextAttemptAt = nowUtc,
        CreatedAt = nowUtc,
        UpdatedAt = nowUtc,
        BatchId = batchId,
        TileIndex = batchId is null ? 0 : tileIndex,
        TileCount = batchId is null ? 0 : tileCount
    };
}
