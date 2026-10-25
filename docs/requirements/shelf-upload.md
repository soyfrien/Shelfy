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

# Status (2026-10-25)

| Criterion | State |
|---|---|
| Camera or library photo | Client work (MAUI app not started) |
| JPEG and PNG only, clear message | Done: validated by content signature; 415 with message |
| 20 MB maximum, clear message | Done: 413 with message, enforced while streaming |
| Stored and a processing message queued | Done with local filesystem storage and a database queue (Blob Storage and Service Bus are later swaps, ADR-008) |
| Upload progress and "processing" status | Status: `GET /api/recognition-jobs/{id}`. Progress UI is client work |
| Requires an authenticated user | Not done (ADR-006); the endpoint is open |
| Failed upload can be retried without duplicates | Done: the same photo on the same shelf returns the existing job |
