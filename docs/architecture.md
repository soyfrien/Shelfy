# Architecture

Read the [ADRs](adr) for the reasoning behind each choice.

## Overview

```text
iOS      Android
   \      /
   .NET MAUI app
        |
ASP.NET Core 8 Web API  (modular monolith, vertical slices)
        |
-----------------------------------------
| Users   | Shelves | Books  | Recognition |
| Trades* | Messaging* | Recommendations* |
-----------------------------------------
        |
PostgreSQL (EF Core 8)

* planned, not in v1
```

## Components

| Layer | Choice |
|---|---|
| Mobile client | .NET MAUI (iOS, Android) |
| API | ASP.NET Core 8 Web API |
| Data | EF Core 8 + PostgreSQL |
| Identity | Microsoft Entra External ID (Auth0 as alternative) |
| Style | Modular monolith, Vertical Slice Architecture |
| Search | PostgreSQL full-text first; Azure AI Search later |
| Hosting | Azure App Service, PostgreSQL Flexible Server, Blob Storage, Service Bus, Azure AI Vision |

## Recognition pipeline

```text
MAUI app
  |  upload photo
  v
API ---> Blob Storage ---> Azure Service Bus queue
                                  |
                                  v
                       Background worker
                                  |  OCR spines (Azure AI Vision)
                                  v
                       Candidate titles / authors
                                  |  resolve
                                  v
                    Google Books / OpenLibrary
                                  |
                                  v
                    Attach books to the user's Shelf
```

## Repository layout (planned)

```text
src/
  Shelfy.Api
  Shelfy.Domain
  Shelfy.Infrastructure
  Shelfy.Features/
    Users/
    Shelves/
      CreateShelf/
      GetShelf/
      UpdateShelf/
    Books/
      AddBook/
      SearchBooks/
    Recognition/
    Trading/        (later)
docs/
```

Each feature folder is a self-contained slice (request, handler, validation, endpoint, tests) so an AI assistant needs only that folder as context.

## Modules

Users, Shelves, Books, Recognition, Trades, Messaging, Recommendations. Each module owns its services and data boundaries; modules talk through explicit interfaces or events, never by reaching into each other's tables. Trades, Messaging and Recommendations are not built in v1.

## Search

- v1/v2: PostgreSQL full-text search.
- Later: Azure AI Search.

## Recommendations (later)

Collaborative filtering first, AI-based later.

## Deployment

```text
.NET 8 API ............ Azure App Service
PostgreSQL ............ Azure Database for PostgreSQL Flexible Server
Photos ................ Azure Blob Storage
Queue ................. Azure Service Bus
OCR ................... Azure AI Vision
```

## Scaling path

Start as a modular monolith. Split a module into its own service only when load or team size demands it, likely after tens of thousands of users.
