# ADR-002: Use .NET MAUI

- Status: Accepted
- Date: 2026-10-24

## Context

The core experience is taking a photo of a bookshelf on a phone. We need iOS and Android from one C# codebase.

## Decision

Use .NET MAUI for the mobile client, sharing models and UI code.

## Consequences

- One codebase for iOS and Android, same language as the backend.
- Camera and file access via MAUI APIs.
- MAUI tooling is less mature than native; keep UI logic simple and testable.
