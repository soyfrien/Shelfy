# Feature

Book Recognition

# User Story

As a reader, I want Shelfy to identify the books in my shelf photo so that I do not have to type them in.

# Acceptance Criteria

- A background worker picks up each queued photo.
- OCR (Azure AI Vision) extracts text from book spines.
- Extracted text is resolved against Google Books and OpenLibrary to an ISBN, title, author and cover.
- Resolved books are attached to the user's Shelf as ShelfBook with status Private.
- Spines that cannot be resolved are listed as unmatched so the user can correct them manually.
- The same book is not added twice from one photo.
- Processing status (queued, processing, complete, failed) is visible to the user.
- Failures are retried and then dead-lettered without losing the photo.

# Status (2026-10-25)

| Criterion | State |
|---|---|
| Background worker picks up each queued photo | Done (`RecognitionWorker`, database queue) |
| OCR extracts text from spines | Done with Azure AI Vision Read client; not yet run against the real service |
| Resolve against Google Books and OpenLibrary | Done, OpenLibrary first, Google Books fallback |
| Attach resolved books as Private ShelfBook | Done |
| Unresolved spines listed as unmatched | Done: kept as `DetectedSpine` rows with status Unresolved; manual correction endpoint not built |
| Same book not added twice from one photo | Done |
| Status visible (queued, processing, complete, failed) | Done: Pending, Processing, Completed, Failed |
| Failures retried then dead-lettered without losing the photo | Done: backoff retries, then Failed; photo kept |

Accuracy on real shelf photos is unmeasured. See [pipeline.md](../pipeline.md).
