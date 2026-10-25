# Recognition pipeline (v1)

Photo to recognized shelf. Decisions: [ADR-007](adr/ADR-007-ocr-and-book-resolution.md),
[ADR-008](adr/ADR-008-database-job-queue-and-pluggable-providers.md).
Requirements: [shelf upload](requirements/shelf-upload.md), [recognition](requirements/recognition.md).

## Flow

```text
POST /api/shelves/{shelfId}/photos          (multipart, field "file")
  | validate: JPEG/PNG by content signature, <= 20 MB, extension consistent
  | stream to IPhotoStorage (SHA-256 + size counted on the way)
  | same photo already on this shelf?  -> return the existing job
  | insert RecognitionJob (Pending)
  v
202 Accepted { jobId, status, statusUrl }

RecognitionWorker (BackgroundService, one job at a time)
  | IJobQueue.ClaimNextAsync          Pending & due, or Processing with an expired lease
  v
RecognitionJobProcessor
  | 1. open photo stream from IPhotoStorage
  | 2. ITextRecognizer.RecognizeAsync        lines (+ polygons, word confidence)
  | 3. SpineTextParser.Parse                 one candidate (title, author, isbn) per spine
  | 4. IBookResolver.ResolveAsync            per spine, sequentially
  | 5. upsert Book by ISBN, Shelf.AddBook (Private, no duplicates), save DetectedSpine rows
  v
IJobQueue.CompleteAsync  (or FailAsync -> retry with backoff, or Failed)

GET /api/recognition-jobs/{jobId}   status, spines (resolved and unresolved), books, attempts, error
```

## Endpoints

| Method | Path | Result |
|---|---|---|
| POST | `/api/shelves/{shelfId}/photos` | 202 with `{ jobId, status, statusUrl, duplicate }`; 400 no file; 404 unknown shelf; 413 over 20 MB; 415 not JPEG/PNG or wrong content type |
| GET | `/api/recognition-jobs/{jobId}` | 200 job; 404 unknown job |

Job JSON: `status` (`Pending`, `Processing`, `Completed`, `Failed`), `attempts`, `maxAttempts`, `retryCount`, `error`,
`booksAdded`, `createdAt`, `startedAt`, `completedAt`, `nextAttemptAt` (while Pending), `spines[]` and `books[]`.
Each spine has `position`, `rawText`, `confidence`, `candidateTitle`, `candidateAuthor`, `candidateIsbn`,
`status` (`Resolved` or `Unresolved`), `resolvedIsbn`, `matchScore`, `matchSource` and `book`.
Unresolved spines are the list the user corrects manually (the correction UI and endpoint are not built yet).

## Job lifecycle

```text
Pending --claim--> Processing --ok--> Completed
   ^                   |
   |  retryable error  +--permanent error or attempts exhausted--> Failed
   +---(backoff)-------+
```

- Backoff after attempt n: `BackoffBaseSeconds * 2^(n-1)`, capped at `BackoffMaxSeconds` (defaults 30 s, 15 min).
- Permanent errors (fail immediately): OCR not configured, Azure Vision 4xx other than 429/408, photo missing, shelf or job gone.
- Retryable: network errors, 429, 5xx, all book resolvers down, timeouts, unexpected exceptions.
- A worker that dies mid-job leaves it `Processing`; after `LeaseMinutes` another claim picks it up. A job whose lease
  expires during its last attempt is marked `Failed`.
- Re-running a job replaces its spines; books are never added to a shelf twice.

## Spine parsing (`SpineTextParser`, pure)

1. Each OCR line gets an orientation and a thickness (font height) from its polygon: horizontal, vertical top-to-bottom
   or vertical bottom-to-top. Noise lines (publisher names, "bestseller", awards, bare numbers) are dropped.
2. Lines of the same orientation are grouped into one spine when they overlap along the text axis and sit side by side
   (edge gap at most 0.6 x the thinner line), or share a column.
3. In a group the thickest line is the title. An author-looking line (2-5 capitalized tokens, initials allowed) is the
   author. A big author name over a smaller non-name title is swapped. Same-size lines continue the title.
4. A valid ISBN in the text is extracted and kept out of the title.
5. Lines without geometry become one candidate each. "Title by Author" and "Title - Author" are split.
6. ALL-CAPS is title-cased. Spines are ordered left to right. At most `MaxSpinesPerPhoto` are kept.

This is a heuristic tuned on synthetic and recorded sample data only. Real shelf photos will need threshold tuning
(`SpineTextParser` constants) and probably measurement. Multi-row shelves are not separated.

## Book resolution

- `OpenLibraryBookResolver`: `search.json?title=&author=` (or `q=isbn:`), no key. `GoogleBooksResolver`: `volumes?q=intitle:+inauthor:`,
  optional key. Both use a polite User-Agent, a timeout and `RetryHandler` (429/5xx/network, exponential backoff, capped `Retry-After`).
- `MatchScorer` scores candidates 0..1: fuzzy title similarity (edit distance and token overlap, subtitle-tolerant,
  ignoring case, accents, punctuation and leading articles) times an author factor `0.55 + 0.45 x authorSimilarity`
  (surname match counts strongly, initials are merged). Without an author the title score is multiplied by 0.9.
- `CompositeBookResolver` takes the first match at or above `MinMatchScore` (default 0.72), OpenLibrary first. If every
  provider is down it throws a retryable error. If title+author finds nothing the processor retries with the title alone.
- ISBNs are normalized to ISBN-13. OpenLibrary lists ISBNs for all editions; an English registration group (978-0/978-1) is preferred.
  Different editions of one book have different ISBNs, so the same title in two editions can appear twice (known limitation).

## Deployment status

Deployed on cosmic2 at `https://shelfy.ppdac.ltd` (2026-10-25). Verified end to end with a synthetic 4-spine image
(`tools/make-test-shelf.py`): Azure AI Vision read all 4 spines, OpenLibrary resolved all 4 (score 1.0), job Completed in
about 2 s. Duplicate upload returns the existing job; a non-image returns 415. Real shelf photos are still untested.

## What is real and what is stubbed

| Part | Status |
|---|---|
| Upload, validation, job status endpoints | Implemented |
| Local filesystem photo storage | Implemented |
| Azure Blob photo storage | **Stub** (throws; select with `Storage__Provider=AzureBlob` only after implementing) |
| Database job queue and worker | Implemented |
| Azure Service Bus queue | Not built (`IJobQueue` is the seam) |
| Azure AI Vision OCR client | Implemented against the documented Image Analysis 4.0 REST contract; verified against the real service (2023-10-01, F0 resource in southcentralus): the response shape matches the parser, rotated spines come back with polygons starting at the text's top-left; live responses are recorded as `Fixtures/AzureVision/read-live-*.json`. Real shelf photos are still untested |
| OpenLibrary resolver | Implemented; verified live from the deployed VM |
| Google Books resolver | Implemented; recorded JSON only. Unkeyed calls from the VM returned 429, so set `Resolution__GoogleBooksApiKey` |
| Authentication / ownership checks | Not built (ADR-006); the upload and job endpoints are open |
| Manual correction of unresolved spines | Not built |
| Image preprocessing (resize, rotate, EXIF) | Not built; the photo goes to OCR as uploaded |

## Configuration

Environment variables use `__` for nesting (for example `AzureVision__Key`). Nothing secret is in the repo.

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings__ShelfyDb` | placeholder in appsettings.json | PostgreSQL |
| `Storage__Provider` | `Local` | `Local` or `AzureBlob` (stub) |
| `Storage__LocalPath` | `data/photos` | Photo directory (use an absolute path, writable by the service user) |
| `AzureVision__Endpoint` | empty | `https://<resource>.cognitiveservices.azure.com` |
| `AzureVision__Key` | empty | Azure AI Vision key; with the endpoint, enables OCR |
| `AzureVision__ApiVersion` | `2023-10-01` | Image Analysis API version |
| `AzureVision__TimeoutSeconds` | `60` | |
| `Recognition__OcrProvider` | `Auto` | `Auto` (Azure when configured, else NotConfigured) or `Fake` (dev only, canned data) |
| `Recognition__WorkerEnabled` | `true` | Run the worker inside the API |
| `Recognition__MaxAttempts` | `4` | Attempts per job (stored on the job at upload) |
| `Recognition__BackoffBaseSeconds` / `BackoffMaxSeconds` | `30` / `900` | Retry backoff |
| `Recognition__LeaseMinutes` / `ProcessingTimeoutMinutes` | `10` / `8` | Crash recovery lease / per-attempt cap |
| `Recognition__PollIntervalSeconds` | `5` | Idle poll interval |
| `Recognition__MinMatchScore` | `0.72` | Acceptance threshold |
| `Recognition__MaxSpinesPerPhoto` | `150` | Bound per photo |
| `Resolution__GoogleBooksApiKey` | empty | Optional |
| `Resolution__UserAgent` | `Shelfy/1.0 (+https://github.com/soyfrien/Shelfy)` | Sent to OpenLibrary / Google; add a contact address |
| `Resolution__OpenLibraryEnabled` / `GoogleBooksEnabled` | `true` / `true` | |
| `Resolution__TimeoutSeconds` / `MaxRetries` / `RetryBaseDelayMilliseconds` | `10` / `2` / `500` | |

## Memory and size bounds

One job at a time. The upload is streamed to storage in 80 KB chunks (hash and size counted on the fly) and the photo is
streamed to OCR from disk. Request body cap 24 MB, photo cap 20 MB, OCR response cap 8 MB, resolver response cap 2 MB,
at most `MaxSpinesPerPhoto` lookups per photo, raw text stored per spine at most 1000 characters.

## Testing

`dotnet test src/Shelfy.sln`. No test touches the network. Resolver and OCR tests use recorded JSON under
`tests/Shelfy.Tests/Fixtures`. Queue and worker tests run the real SQL against SQLite (a real relational provider, so
`ExecuteUpdate` and the conditional claim run for real, including a concurrent-claim test). Endpoint tests run the real
API host with `WebApplicationFactory`. PostgreSQL-specific behaviour is not covered by the suite; the migration and the
idempotent script were applied to PostgreSQL 16 by hand once, see the notes in the commit history.
