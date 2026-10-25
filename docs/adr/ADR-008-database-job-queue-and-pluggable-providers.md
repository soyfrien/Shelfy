# ADR-008: Database-backed job queue and pluggable recognition providers for v1

- Status: Accepted
- Date: 2026-10-25

## Context

ADR-007 describes the target pipeline: Blob Storage, an Azure Service Bus queue, Azure AI Vision and
Google Books / OpenLibrary. The first production host is a single small VM (about 1 GB of RAM free, PostgreSQL on the
same machine) with neither Service Bus nor Blob Storage. The pipeline still has to work end to end there, and the cloud
services must be pluggable later without rewriting the feature.

## Decision

- Every external dependency of the pipeline sits behind an interface in `Shelfy.Features/Recognition`:
  `IPhotoStorage`, `IJobQueue`, `ITextRecognizer`, `IBookResolver`.
- v1 implementations:
  - `IPhotoStorage`: `LocalFileSystemPhotoStorage` (default). `AzureBlobPhotoStorage` is a stub that throws.
  - `IJobQueue`: `DbJobQueue`, backed by the `RecognitionJobs` table. A `BackgroundService` (`RecognitionWorker`) polls it
    and processes one job at a time. A Service Bus implementation replaces it later.
  - `ITextRecognizer`: `AzureVisionTextRecognizer` (Image Analysis 4.0 Read over `HttpClient`),
    `NotConfiguredTextRecognizer` (fails the job with a helpful message) and `FakeTextRecognizer` (tests and dev).
  - `IBookResolver`: `OpenLibraryBookResolver` first, `GoogleBooksResolver` as fallback, combined by `CompositeBookResolver`.
- Two entities are added to the domain, as terms of the Recognition module: `RecognitionJob` (one uploaded photo and its
  processing state) and `DetectedSpine` (one spine found in a photo, with raw OCR text, parsed candidate and resolution).
- Claiming a job is a single conditional `UPDATE ... WHERE <still claimable>` (EF Core `ExecuteUpdate`), which is atomic on
  PostgreSQL and every other relational provider. It is deliberately not `FOR UPDATE SKIP LOCKED`: that is
  PostgreSQL-specific, cannot be exercised by the portable test setup, and is not needed at one worker per instance.
  A lease (`LockedUntil`) recovers jobs from crashed workers.
- Retries use exponential backoff stored on the job (`NextAttemptAt`), bounded by `MaxAttempts`. A job that runs out of
  attempts becomes `Failed` (dead-lettered); its photo is kept.
- A job is idempotent: a retry replaces the spines of the interrupted attempt, and a book is never added to a shelf twice.
- Re-uploading the same photo (same SHA-256) to the same shelf returns the existing job unless that job `Failed`.

## Consequences

- Works on the existing VM with no new Azure resources.
- Polling adds a small constant database load (two cheap indexed queries every few seconds when idle).
- Moving to Service Bus or Blob Storage is an implementation swap behind an interface plus configuration, not a redesign.
- The worker runs inside the API process by default. `Recognition__WorkerEnabled=false` disables it, for example to run
  the worker as a separate process later.
- Matching is heuristic. Spine grouping and fuzzy scoring need tuning against real photos; see `docs/pipeline.md`.
