# Shelfy

Photograph your bookshelf and Shelfy turns it into a structured digital shelf.

Computer vision and OCR read the book spines. Each book is matched to metadata (ISBN, author, cover) from Google Books and OpenLibrary. Later versions turn users into nodes in a social "library fabric" where books can be loaned, sold, recommended and discovered.

Built with C# and .NET 8, developed mostly with AI coding assistants (Grok, GPT, Claude) that produce code from written specs.

## MVP v1 scope

In scope:
- .NET MAUI mobile app (iOS and Android)
- Photo upload of a bookshelf
- OCR and metadata resolution of book spines
- Digital bookshelf
- Shareable public shelf page

Out of scope for v1: trading, messaging, social graph, recommendations.

## Tech stack

ASP.NET Core 8 Web API, .NET MAUI, EF Core 8 with PostgreSQL, modular monolith with Vertical Slice Architecture, Microsoft Entra External ID, Azure (App Service, Blob Storage, Service Bus, AI Vision).

## Docs index

- [Vision](docs/vision.md)
- [Architecture](docs/architecture.md)
- [Domain](docs/domain.md)
- [System prompt for AI sessions](docs/system-prompt.md)
- [AI prompt workflow](docs/workflow.md)
- Architecture decisions: [docs/adr](docs/adr)
  - [ADR-001 PostgreSQL](docs/adr/ADR-001-use-postgresql.md)
  - [ADR-002 .NET MAUI](docs/adr/ADR-002-use-dotnet-maui.md)
  - [ADR-003 ASP.NET Core 8](docs/adr/ADR-003-use-aspnet-core-8.md)
  - [ADR-004 Vertical Slice Architecture](docs/adr/ADR-004-use-vertical-slice-architecture.md)
  - [ADR-005 Modular monolith](docs/adr/ADR-005-use-modular-monolith.md)
  - [ADR-006 Entra External ID](docs/adr/ADR-006-use-entra-external-id.md)
  - [ADR-007 Azure AI Vision OCR and book resolution](docs/adr/ADR-007-ocr-and-book-resolution.md)
- Requirements: [shelf upload](docs/requirements/shelf-upload.md), [recognition](docs/requirements/recognition.md), [public shelf page](docs/requirements/public-shelf-page.md)

## Status

Documentation only. No code projects yet.
