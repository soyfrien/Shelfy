# ADR-001: Use PostgreSQL

- Status: Accepted
- Date: 2026-10-24

## Context

Shelfy needs a relational store for users, books, shelves and later trades, plus basic search. We want something simple, inexpensive and well supported by EF Core.

## Decision

Use PostgreSQL via EF Core 8 (Azure Database for PostgreSQL Flexible Server). Use PostgreSQL full-text search first; move to Azure AI Search later if needed.

## Consequences

- Simple, mature, low cost.
- Strong EF Core support.
- Full-text search avoids a second system in v1.
- Migrating search to Azure AI Search later needs an indexing pipeline.
