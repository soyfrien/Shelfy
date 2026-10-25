using Shelfy.Features.Recognition.GetRecognitionJob;

namespace Shelfy.Features.Recognition.GetUploadBatch;

/// <param name="Part">1-based position in the batch.</param>
/// <param name="Status">Pending, Processing, Completed or Failed.</param>
/// <param name="Error">Last error while retrying or after the final failure.</param>
public record BatchPartDto(int Part, Guid JobId, string Status, int Attempts, int MaxAttempts, string? Error,
    DateTime? NextAttemptAt, int SpineCount);

/// <summary>
/// Progress of one upload batch (the tiles of a wide shelf photo, or a single photo).
/// </summary>
/// <param name="Parts">How many photos the client announced.</param>
/// <param name="Received">How many distinct parts have been uploaded so far.</param>
/// <param name="Done">True when every announced part was uploaded and is Completed or Failed.</param>
/// <param name="BooksAdded">Books the batch added to the shelf.</param>
/// <param name="Spines">
/// De-duplicated spines of the latest job of each part: reads of the same spine in overlapping tiles are merged,
/// resolved spines are listed once per book, unresolved ones once per physical spine.
/// </param>
public record UploadBatchResponse(
    Guid BatchId,
    Guid ShelfId,
    int Parts,
    int Received,
    int Pending,
    int Processing,
    int Completed,
    int Failed,
    bool Done,
    int BooksAdded,
    IReadOnlyList<BatchPartDto> Jobs,
    IReadOnlyList<DetectedSpineDto> Spines,
    IReadOnlyList<ResolvedBookDto> Books);
