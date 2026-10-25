# Feature

Shelf Upload

# User Story

As a reader, I want to photograph my bookshelf so that Shelfy can build my digital library.

# Acceptance Criteria

- The user can take a photo with the camera or pick one from the library.
- Accept JPEG and PNG only; reject other formats with a clear message.
- Maximum file size is 20 MB; reject larger files with a clear message.
- The photo is stored in Blob Storage and a processing message is queued.
- The user sees upload progress and a "processing" status.
- The upload requires an authenticated user.
- A failed upload can be retried without creating duplicates.
