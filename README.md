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
  - [ADR-008 Database job queue and pluggable providers](docs/adr/ADR-008-database-job-queue-and-pluggable-providers.md)
  - [ADR-009 Tiled uploads and batches](docs/adr/ADR-009-tiled-uploads-and-batches.md)
  - [ADR-010 Clearing a shelf](docs/adr/ADR-010-clearing-a-shelf-hide-or-delete.md)
  - [ADR-011 Tap to fix and remembered corrections](docs/adr/ADR-011-tap-to-fix-and-remembered-corrections.md)
  - [ADR-012 Accounts, physical copies and lending by mail](docs/adr/ADR-012-accounts-copies-and-lending.md)
- [Recognition pipeline](docs/pipeline.md)
- [Next: MAUI app](docs/next-maui-app.md)
- Requirements: [shelf upload](docs/requirements/shelf-upload.md), [recognition](docs/requirements/recognition.md), [public shelf page](docs/requirements/public-shelf-page.md)

## Repository layout

```text
src/
  Shelfy.sln
  Shelfy.Api              ASP.NET Core 8 host: Swagger, /health, wires modules together (no business logic)
  Shelfy.Domain           User, Shelf, Book, ShelfBook, AvailabilityStatus, RecognitionJob, DetectedSpine, UserCorrection,
                          Copy, CopyCode, CopyMovement, Loan and the Lending state machine (see docs/domain.md, ADR-012)
  Shelfy.Infrastructure   EF Core 8 + Npgsql DbContext, entity configurations, migrations
  Shelfy.Features         Vertical slices, one folder per module
    Users/CreateGuest       POST /api/guests   (anonymous guest for the web UI)
    Users/Account           POST /api/account/register | login | logout, GET /api/account/me, PUT /api/account/profile (Identity cookie, ADR-012)
    Shelves/CreateShelf     POST /api/shelves
    Shelves/GetShelf        GET  /api/shelves/{shelfId}   (404 for a hidden shelf)
    Shelves/HideShelf       POST /api/shelves/{shelfId}/hide   (soft clear, 204, idempotent, ADR-010)
    Shelves/DeleteShelf     DELETE /api/shelves/{shelfId}      (hard clear incl. photo files, 204, idempotent, ADR-010)
    Books/SearchBooks       GET  /api/books/search?q=...&take=20
    Recognition/
      UploadShelfPhoto      POST /api/shelves/{shelfId}/photos   (multipart "file", 202 + job id)
      GetRecognitionJob     GET  /api/recognition-jobs/{jobId}
      GetUploadBatch        GET  /api/shelves/{shelfId}/batches/{batchId}   (progress and merged result of a tiled photo)
      GetShelfUnmatched     GET  /api/shelves/{shelfId}/unmatched   (spines of the shelf's photos that matched no book, noise filtered; read-only)
      FixSpine              GET  /api/shelves/{shelfId}/spines/{spineId}/suggestions[?q=]   (tap to fix: candidate books / search box)
                            POST /api/shelves/{shelfId}/spines/{spineId}/resolve   (pick a book; remembered as a correction, ADR-011)
                            POST /api/shelves/{shelfId}/spines/{spineId}/dismiss   ("not a book")
    Lending/                (ADR-012) physical copies, borrow / reserve / loan steps, QR label, copy page
      Copies                POST /api/shelves/{shelfId}/books/{bookId}/copies, GET /api/shelves/{shelfId}/copies,
                            GET /api/copies/{id} | /api/copies/by-code/{code}, PUT /api/copies/{id}, GET /api/copies/{id}/label.svg
      Loans                 POST /api/copies/{id}/borrow | /reserve, POST /api/copies/by-code/{code}/received,
                            GET /api/loans, GET /api/loans/{id}, POST /api/loans/{id}/{approve|decline|cancel|mailed|received|...}
      CopyPage              GET /c/{code}   (where a copy's QR sticker points)
      Storage/ Ocr/ Parsing/ Resolution/ Worker/   pipeline pieces behind interfaces, see docs/pipeline.md
tests/
  Shelfy.Tests            xunit: domain, slice handlers, parser, resolvers, queue/worker (SQLite), API endpoints; recorded JSON fixtures
db/                       idempotent schema script (generate with db/generate-schema.sh)
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

Tests need no database server and never touch the network: handler tests use the EF Core in-memory provider, queue, worker and endpoint tests use a temporary SQLite file, and resolver/OCR tests use recorded JSON.

## Migrations

```bash
dotnet ef migrations add <Name> --project src/Shelfy.Infrastructure --startup-project src/Shelfy.Api --output-dir Migrations
db/generate-schema.sh        # regenerates db/schema.idempotent.sql (BOM-free)
```

Migrations so far: `InitialCreate`, `AddRecognitionPipeline` (RecognitionJobs, DetectedSpines), `AddUploadBatches` (BatchId, TileIndex, TileCount on RecognitionJobs),
`AddShelfHidden`, `AddUserCorrections`, `AddAccountsAndLending` (AspNet* Identity tables, Copies, CopyMovements, Loans, Users.MailingAddress).

## Recognition pipeline

Upload a photo, a background worker reads the spines, matches books and adds them to the shelf as Private.

```bash
# development without Azure: canned OCR data, real OpenLibrary lookups
export Recognition__OcrProvider=Fake Storage__LocalPath=/tmp/shelfy-photos
dotnet run --project src/Shelfy.Api
curl -F "file=@shelf.jpg;type=image/jpeg" http://localhost:5165/api/shelves/<shelfId>/photos   # 202 { jobId, statusUrl }
curl http://localhost:5165/api/recognition-jobs/<jobId>
```

For real OCR set `AzureVision__Endpoint` and `AzureVision__Key`. Without them, jobs fail with a message saying so.
All configuration keys, the flow and the limits are in [docs/pipeline.md](docs/pipeline.md).

## Web UI

A plain HTML/CSS/JS page (no build step) is served by the API from `src/Shelfy.Api/wwwroot`:

- `/` home page: pick or take a photo. The browser decodes it (this also converts HEIC) and either downscales it (normal photos,
  longest side 3200 px, JPEG) or, for a wide or large shelf, cuts it into overlapping tiles of at most 2400 px
  ([ADR-009](docs/adr/ADR-009-tiled-uploads-and-batches.md), [how the thresholds work](docs/pipeline.md)). It creates an anonymous guest and a shelf
  (`POST /api/guests`, `POST /api/shelves`), uploads the tiles with a shared batch id, and polls the batch: "Reading part k of n", a progress bar,
  the shelf filling up as parts complete, a Retry button for a part that failed, then the books and the spines that could not be matched.
  The guest id and shelf id are kept in `localStorage`. `node tools/test-tiles.js` checks the tiling plan.
- Search (home page and `/s/{shelfId}`): a sticky "Search title or author" box above the cover grid filters live while you type, ignoring case,
  accents and punctuation; several words match in any order ("hugo miserables"), matches are highlighted, and it shows "N of M books", a clear (x)
  button, a sort choice (title or author A-Z) and a "No books match" state. On the home page it also searches the "Spines I couldn't match" list
  (so a book that was read but not recognized can still be found, also after a reload) and offers a jump link to the matching entries.
  Client-side only (`wwwroot/search.js`, `shared.js`); `node tools/test-search.js` tests the matching, `tools/ui-check-search.js` drives it in headless mobile Chrome.
- "Clear shelf" (under the books): a bottom sheet offers **Hide shelf** (default: the shelf is no longer shown or shared, data is kept) or
  **Delete everything** (red: books on the shelf, detected spines, jobs and stored photos are erased; the shared Books catalog stays).
  Afterwards the page forgets the shelf and starts fresh with a "Shelf cleared" note ([ADR-010](docs/adr/ADR-010-clearing-a-shelf-hide-or-delete.md)).
- `/s/{shelfId}` shareable shelf page (title, author, cover grid, link-preview tags; unknown, hidden or deleted shelf gives a 404 page).
  The signed-in owner of an account shelf can turn on **Show my shelf in search** (`PATCH /api/shelves/{id}` with `{ "isPublic": true }`).
  Guest shelves stay out of search. A private shelf can still be opened by its link.

## Accounts and lending (ADR-012)

Sign in or create an account (`/signin.html`, email + password). A shelf made while signed in belongs to your account: on its page
(`/s/{id}`) tap a book and choose **Lend this book** to give that physical copy a code and a printable QR label. Others tap a book to
**Ask to borrow** (or **Reserve** when it is out); you approve, mail it, and they scan the sticker (`/c/{code}`) and tap **I received this**.
**My loans** (`/loans.html`) shows every request with its next step and your private mailing address, which only the other person of an
approved loan ever sees. Copies go back to their owner after each loan; pass-along to the next reader is a per-copy option the owner approves.
Guest shelves keep working without an account but cannot lend.

## Status

Backend: domain model, PostgreSQL data layer, shelf and book-search slices, and the v1 recognition pipeline
(photo upload, job status, background worker, OCR, spine parsing, book resolution, tiles and batch progress for wide shelves) are built and tested.
Stubbed or not verified: Azure Blob storage is a stub, Service Bus is replaced by a database queue, and the Azure AI Vision
client has not been run against the real service (see [docs/pipeline.md](docs/pipeline.md)).
The Azure AI Vision client has since been run against the real service (see docs/pipeline.md) and a mobile web UI is live at https://shelfy.ppdac.ltd/.
Accounts (ASP.NET Core Identity, ADR-012) protect account shelves and lending; guest shelves and reads stay open. Entra External ID (ADR-006) is deferred. The MAUI app is next, see [docs/next-maui-app.md](docs/next-maui-app.md).
