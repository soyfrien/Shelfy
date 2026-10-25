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
- [Next: MAUI app](docs/next-maui-app.md)
- Requirements: [shelf upload](docs/requirements/shelf-upload.md), [recognition](docs/requirements/recognition.md), [public shelf page](docs/requirements/public-shelf-page.md)

## Repository layout

```text
src/
  Shelfy.sln
  Shelfy.Api              ASP.NET Core 8 host: Swagger, /health, wires modules together (no business logic)
  Shelfy.Domain           User, Shelf, Book, ShelfBook, AvailabilityStatus (frozen terms, see docs/domain.md)
  Shelfy.Infrastructure   EF Core 8 + Npgsql DbContext, entity configurations, migrations
  Shelfy.Features         Vertical slices, one folder per module
    Users/  Recognition/    module placeholders (no slices yet)
    Shelves/CreateShelf     POST /api/shelves
    Shelves/GetShelf        GET  /api/shelves/{shelfId}
    Books/SearchBooks       GET  /api/books/search?q=...&take=20
tests/
  Shelfy.Tests            xunit: domain tests and slice handler tests (EF Core in-memory)
docs/                     vision, architecture, domain, ADRs, requirements, AI workflow
docker-compose.yml        local PostgreSQL
```

Each slice folder holds its request and response records, handler, and endpoint mapping. Handlers contain the logic; endpoints only translate HTTP. See [docs/adr/ADR-004](docs/adr/ADR-004-use-vertical-slice-architecture.md).

## Run locally

Prerequisites: .NET 8 SDK, Docker.

```bash
docker compose up -d                      # PostgreSQL on localhost:5432
dotnet tool restore                       # installs dotnet-ef
dotnet ef database update --project src/Shelfy.Infrastructure --startup-project src/Shelfy.Api
dotnet run --project src/Shelfy.Api       # http://localhost:5165/swagger, health at /health
```

The local connection string in `src/Shelfy.Api/appsettings.json` is a placeholder for the docker-compose database only. Override it with the `ConnectionStrings__ShelfyDb` environment variable or user secrets. Never commit real secrets.

## Build and test

```bash
dotnet build src/Shelfy.sln
dotnet test src/Shelfy.sln
```

Tests use the EF Core in-memory provider, so they need no database.

## Migrations

```bash
dotnet ef migrations add <Name> --project src/Shelfy.Infrastructure --startup-project src/Shelfy.Api --output-dir Migrations
```

## Status

Backend scaffold only: the solution builds, the example slices (CreateShelf, GetShelf, SearchBooks) are stubs, and authentication (ADR-006), Blob Storage, Service Bus and the recognition worker (ADR-007) are not wired up yet. The MAUI app is next, see [docs/next-maui-app.md](docs/next-maui-app.md).
