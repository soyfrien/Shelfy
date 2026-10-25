namespace Shelfy.Features.Recognition.UploadShelfPhoto;

/// <param name="Duplicate">True when the same photo was already uploaded to this shelf and its existing job is returned (safe retry).</param>
public record UploadShelfPhotoResponse(Guid JobId, string Status, string StatusUrl, bool Duplicate);
