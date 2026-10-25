namespace Shelfy.Features.Recognition.UploadShelfPhoto;

// TODO(ADR-006): require an authenticated user who owns the shelf.
/// <param name="FileName">Client-supplied name; only its extension is consulted, and only for consistency with the content.</param>
/// <param name="DeclaredLength">Length the client declared, if known; used to reject oversized files before reading.</param>
/// <param name="Content">The file bytes. Read once, sequentially, and not disposed by the handler.</param>
/// <param name="BatchId">Optional. Photos that belong together (the tiles of one wide shelf photo) share a client-chosen id.</param>
/// <param name="Part">With a batch: 1-based position of this photo in the batch.</param>
/// <param name="Parts">With a batch: how many photos the batch has (1..<see cref="UploadShelfPhotoRequest.MaxParts"/>).</param>
public record UploadShelfPhotoRequest(Guid ShelfId, string? FileName, long? DeclaredLength, Stream Content,
    Guid? BatchId = null, int Part = 0, int Parts = 0)
{
    public const int MaxParts = 24;
}
