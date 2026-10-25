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
