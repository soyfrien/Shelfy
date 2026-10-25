# Recognition pipeline (v1)

Photo to recognized shelf. Decisions: [ADR-007](adr/ADR-007-ocr-and-book-resolution.md),
[ADR-008](adr/ADR-008-database-job-queue-and-pluggable-providers.md),
[ADR-009](adr/ADR-009-tiled-uploads-and-batches.md) (wide photos, tiles and batches).
Requirements: [shelf upload](requirements/shelf-upload.md), [recognition](requirements/recognition.md).

## Flow

```text
POST /api/shelves/{shelfId}/photos          (multipart, field "file"; optional batchId, part, parts)
  | validate: JPEG/PNG by content signature, <= 20 MB, extension consistent
  | stream to IPhotoStorage (SHA-256 + size counted on the way)
  | same photo already on this shelf (and in this batch)?  -> return the existing job
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
  |    (tiles of a batch: drop repeats and fragments of spines other tiles of the batch already have, see "Tiles")
  v
IJobQueue.CompleteAsync  (or FailAsync -> retry with backoff, or Failed)

GET /api/recognition-jobs/{jobId}   status, spines (resolved and unresolved), books, attempts, error
GET /api/shelves/{shelfId}/batches/{batchId}   progress per part + merged, de-duplicated spines and books of the batch
```

## Endpoints

| Method | Path | Result |
|---|---|---|
| POST | `/api/shelves/{shelfId}/photos` | 202 with `{ jobId, status, statusUrl, duplicate }`; 400 no file; 404 unknown shelf; 413 over 20 MB; 415 not JPEG/PNG or wrong content type |
| GET | `/api/recognition-jobs/{jobId}` | 200 job; 404 unknown job |
| GET | `/api/shelves/{shelfId}/batches/{batchId}` | 200 batch progress; 404 unknown batch (or batch of another shelf) |
| POST | `/api/shelves/{shelfId}/hide` | 204 (also when already hidden); 404 unknown shelf. Soft clear, see below |
| DELETE | `/api/shelves/{shelfId}` | 204 always (also for an unknown or already deleted shelf). Hard clear, see below |

A **hidden** shelf behaves like an unknown one for the public API: `GET /api/shelves/{id}`, `/s/{id}`, uploads (404 `Shelf not found.`),
batch progress and job status all answer 404. Nothing is erased; an operator can restore it with `UPDATE "Shelves" SET "IsHidden" = false, "HiddenAt" = NULL WHERE "Id" = '...'`.
**Delete** removes the shelf's `ShelfBooks`, `RecognitionJobs`, `DetectedSpines` and stored photos (every file under `shelves/{shelfId}`);
`Books` (shared catalog) and the anonymous `Users` row stay. Effect on in-flight work: a job still `Pending` on a hidden shelf is not read
(no OCR call) and fails once with "The shelf was cleared before this photo was read." (permanent, no retry); a job being processed when the
shelf is deleted finds nothing to save, fails quietly without a retry, and adds nothing. See ADR-010.

Upload form fields (all optional together): `batchId` (GUID, chosen by the client), `part` (1-based) and `parts` (1..24).
With a `batchId`, `part` and `parts` are required and `1 <= part <= parts`; otherwise 400. Without `batchId` nothing changes.
Batch JSON: `parts` (announced), `received` (distinct parts uploaded so far), `pending`, `processing`, `completed`, `failed`, `done`
(all parts uploaded and none pending or processing), `booksAdded`, `jobs[]` (`part`, `jobId`, `status`, `attempts`, `error`,
`nextAttemptAt`, `spineCount`; the newest job per part, so a re-uploaded failed part replaces its old job), `spines[]` and
`books[]` (merged: resolved spines once per book, unresolved ones once per physical spine).

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
- Permanent errors (fail immediately): OCR not configured, Azure Vision 4xx other than 429/408, photo missing, shelf or job gone, shelf hidden (cleared) by its owner.
- Retryable: network errors, 429, 5xx, all book resolvers down, timeouts, unexpected exceptions.
- A worker that dies mid-job leaves it `Processing`; after `LeaseMinutes` another claim picks it up. A job whose lease
  expires during its last attempt is marked `Failed`.
- Re-running a job replaces its spines; books are never added to a shelf twice.

## Tiles: wide and large photos (ADR-009)

The web UI (`wwwroot/tiles.js`, `planTiles`) decides in the browser, after decoding the photo (EXIF rotation applied):

| Photo (w x h) | Result |
|---|---|
| long side <= 2000 px | one image |
| long/short <= 1.6 and long side <= 4096 px (normal 4:3 or 3:2 phone photos) | one image, long side scaled to 3200 px as before |
| anything wider/larger (ratio > 1.6, or long side > 4096 px) | tiles |

Tiling cuts along the long axis, so a tall photo of a bookcase (portrait) is cut into horizontal bands. The tile length aims at
1.5 x the short side but at most 2400 px: `n = max(2, ceil((long - target) / (target x 0.78)) + 1)` tiles (at most 12), the tile length is
then set so that `n` tiles with 22% overlap cover the photo exactly. Every tile is encoded as JPEG (quality 0.9), its longest side
capped at 2400 px. Examples: 8000 x 2000 gives 4 tiles of 2395 x 2000 (overlap 527 px, wider than any spine); 3200 x 1800 gives
2 tiles; 12000 x 3000 gives 7. A spine up to the overlap width is always whole in at least one tile.

The UI uploads the tiles one after another with the same `batchId` (`part` k of `parts` n) and polls the batch endpoint every
1.5 s while work is going on (2.5 s otherwise; backing off up to 8 s on errors; patience of 3 min plus 90 s per part). It shows
"Reading part k of n", a progress bar, one chip per part, and refreshes the shelf (`GET /api/shelves/{id}`) every time a part completes or the
book count changes, so books appear while later tiles are still being read. A part that failed (upload or recognition) keeps
the partial result on screen and shows its error with a Retry button that uploads that tile again.

Order matters little: the worker takes jobs oldest first, one at a time, and the de-duplication works in either order.

### De-duplication across tiles (`SpineDeduper`, `RecognitionJobProcessor`)

- **Resolved books** are upserted by ISBN-13 and `Shelf.AddBook` ignores a book already on the shelf, so two tiles that resolve the same
  book still give one `ShelfBook` (tested). The batch response lists a resolved book once.
- **Unresolved reads** are compared with all reads of the *other* jobs of the same shelf and batch. Reads are normalized (case, accents,
  punctuation) into tokens; tokens match when equal, one edit apart (>= 5 letters) or one is a prefix of the other (>= 4 letters, a word cut by the tile edge). Two reads are the same spine when
  their Dice similarity is >= 0.8, or one is a *fragment*: all its tokens match a run that starts or ends the longer read, with >= 2 tokens (or one cut word).
  "Dune" / "Dune Messiah" and "Foundation" / "Foundation and Empire" are different.
- A new unresolved read is dropped when a peer is resolved and explains it (the read's text, or the book title with the author, matches), or when a peer read the same
  spine at least as well; when it is better (more letters x higher confidence) it replaces the peers' fragments. A newly resolved read removes
  matching unresolved fragments from other tiles.
- In a tiled photo (`parts > 1`) unresolved reads with fewer than 4 letters, no word of 3+ letters, or mean confidence below 0.35 are not listed (edge garbage). Single photos keep every read, as before.
- Text matching is never used to merge two resolved books.

### Azure Vision rate limit and 429

`OcrThrottle` (singleton) spaces OCR calls by `AzureVision__MinIntervalMilliseconds` (3200 ms, about 18 calls a minute against the free tier's 20).
On a 429 the client waits for `Retry-After` (default 5 s, 10 s; at most 30 s) and sends the same image again, up to `AzureVision__Max429Retries`
(2) times, and also pushes the throttle back so that the next jobs wait too. If it is still 429 the job fails as retryable and the
queue's backoff (30 s, 60 s, ...) applies, as before.

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
about 2 s. Duplicate upload returns the existing job; a non-image returns 415.

Tiles and batches (ADR-009) verified live on the same day with a synthetic 6000 x 1500 shelf of 16 real books
(`python3 tools/make-test-shelf.py --wide --tiles`; six spines straddle a tile edge): 4 tiles, 20 spine reads, all 16 books found, each once
(16 `ShelfBooks`, no duplicates, no unmatched entries), batch done in about 13-19 s, no 429. A headless mobile-sized Chrome run of the UI
showed "Uploading part k of 4", "Reading part k of 4", the progress bar and chips, and the shelf growing 0, 6, 10, 15, 16 books as parts
completed; a part whose upload was broken on purpose showed its error with a Retry button and the partial shelf, and Retry completed the batch
(16 books); a normal 4-spine photo still goes up as one image. Real phone photos of real shelves are still unverified: on the first real shelf photos
(two photos, 230 spine reads) 57 reads matched 56 books, so the parser needs tuning on real data.
A finding from the first live run: the generator put the author line far from a long, small-font title, the parser read it as a separate
"spine", and the lone author name resolved to a book called "Philip K. Dick". Since then an author-only read in a tile is not looked up
(see above) and the generator places the author under the title. The parser's grouping of far-apart lines is untouched.

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
| Image preprocessing (resize, rotate, EXIF, tiling) | In the browser only (`wwwroot/app.js`, `tiles.js`); the server sends the photo to OCR as uploaded |

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
| `AzureVision__MinIntervalMilliseconds` | `3200` | Minimum gap between OCR calls (all jobs); `0` on a paid tier |
| `AzureVision__Max429Retries` | `2` | In-place retries of a 429 (Retry-After, at most 30 s) before the queue's backoff |
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
