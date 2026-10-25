# ADR-005: Use a modular monolith

- Status: Accepted
- Date: 2026-10-24

## Context

Microservices add operational cost that is unjustified for an MVP. We still want clear boundaries so the system can split later.

## Decision

Build one deployable .NET 8 application with modules: Users, Shelves, Books, Recognition, Trades, Messaging, Recommendations. Each module owns its services and data boundaries. The recognition worker runs as a background process using the same codebase.

## Consequences

- Simple deployment and debugging.
- Can reach tens of thousands of users before splitting.
- Needs discipline to keep module boundaries honest.
- Trades, Messaging and Recommendations are deferred past v1.
