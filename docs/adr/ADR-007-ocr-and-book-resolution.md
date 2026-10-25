# ADR-007: Azure AI Vision OCR with Google Books and OpenLibrary resolution

- Status: Accepted
- Date: 2026-10-24

## Context

Book spines must be read from photos and matched to real books with ISBN, author and cover.

## Decision

Pipeline: MAUI uploads photo, API stores it in Blob Storage and enqueues a message on Azure Service Bus. A background worker runs Azure AI Vision OCR, then resolves candidate title/author text against Google Books and OpenLibrary, and attaches matched books to the shelf. Unmatched spines are surfaced for manual correction.

## Consequences

- Managed OCR, no model training in v1.
- Asynchronous processing keeps uploads fast.
- Dependence on third-party APIs; add caching and rate-limit handling.
- Resolution accuracy needs measuring; manual correction is part of the UX.

## Implementation status (2026-10-25)

Implemented in v1 behind interfaces; see [ADR-008](ADR-008-database-job-queue-and-pluggable-providers.md) and [docs/pipeline.md](../pipeline.md).

- Implemented: upload and job endpoints, the background worker, Azure AI Vision Read client, OpenLibrary and Google Books resolvers, spine parsing and fuzzy matching.
- Substituted for now: the queue is a database table (not Azure Service Bus) and photos go to the local filesystem (not Blob Storage).
- Stubbed: `AzureBlobPhotoStorage`.
- Unverified: the Azure AI Vision client has not been run against the real service.
- Not built: manual correction of unmatched spines, caching of lookups.
