namespace Shelfy.Features.Recognition.UploadShelfPhoto;

// TODO(ADR-006): require an authenticated user who owns the shelf.
/// <param name="FileName">Client-supplied name; only its extension is consulted, and only for consistency with the content.</param>
/// <param name="DeclaredLength">Length the client declared, if known; used to reject oversized files before reading.</param>
/// <param name="Content">The file bytes. Read once, sequentially, and not disposed by the handler.</param>
public record UploadShelfPhotoRequest(Guid ShelfId, string? FileName, long? DeclaredLength, Stream Content);
