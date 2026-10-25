# ADR-010: Clearing a shelf: soft "hide" by default, hard "delete" on request, both without sign-in

- Status: Accepted
- Date: 2026-10-25

## Context

Users want a way to empty out and start over ("Clear shelf"). There is no authentication yet (ADR-006), so anybody who knows a
shelf id can call any shelf endpoint, including a destructive one. Shelf ids are random GUIDs and are only shared by the owner
through the share link, which is the same level of protection reads already have. A mistaken tap should be recoverable, but
people also have a legitimate wish to really erase photos of their home.

## Decision

1. **Two actions, soft is the default.** The "Clear shelf" button opens a confirm sheet with **Hide shelf** (the prominent default)
   and **Delete everything** (red, with the consequence spelled out). Cancel does nothing.
2. **Hide** (`POST /api/shelves/{id}/hide`, 204): sets `Shelves.IsHidden` and `HiddenAt`. Every public read of the shelf answers 404
   (`GET /api/shelves/{id}`, `/s/{id}`, uploads, batch progress, job status), so a hidden shelf is indistinguishable from an unknown
   one. Nothing is deleted, so an operator can undo it with one SQL update. Idempotent (the first `HiddenAt` is kept); 404 for an unknown shelf.
3. **Delete** (`DELETE /api/shelves/{id}`, 204): removes `DetectedSpines`, `RecognitionJobs`, `ShelfBooks` and the `Shelves` row in one
   transaction, then the stored photos (each job's key, then the whole `shelves/{id}` folder to catch a photo uploaded at that very moment).
   The shared `Books` catalog is never touched (rows are keyed by ISBN and shared between shelves), nor is the anonymous `Users` row.
   Idempotent: 204 for an unknown or already deleted shelf. Rows go before files: a leftover file is only disk space, a missing file behind a live row would be a bug.
4. **In-flight recognition.** The processor refuses a job whose shelf is hidden (permanent failure, no OCR call, no retry). If the shelf is
   deleted while a job is being processed, the final save fails on the missing rows; the processor recognizes that case, fails the job
   quietly without retry, and nothing is resurrected (the job row is gone, so the queue has nothing to retry).
5. **The browser** forgets the shelf id (`localStorage`) after either action, returns to the empty start state and shows "Shelf cleared".
   The shared page `/s/{id}` stays read-only: there is no clear button there.
6. **Schema:** migration `AddShelfHidden` adds `IsHidden boolean NOT NULL DEFAULT false` and `HiddenAt timestamptz NULL`. It is additive:
   the previous build keeps working against the new columns, so the migration is applied before the new build.

## Consequences

- Until sign-in exists, anyone holding a shelf id can hide or delete that shelf. This is accepted for the same reason reads are open;
  the id is unguessable and the endpoints carry `TODO(ADR-006)` to require the owner. Rate limiting stays with nginx.
- A hidden shelf's photos stay on disk. The footer in the UI says nothing about retention yet; if privacy requires it, a janitor job
  can hard-delete shelves hidden for longer than N days.
- Hiding does not hide the books themselves: the `Books` catalog is public reference data (title, author, ISBN, cover).
- A guest `Users` row stays after its last shelf is deleted (no personal data is stored on it).
