# ADR-004: Use Vertical Slice Architecture

- Status: Accepted
- Date: 2026-10-24

## Context

The project is built mostly by AI assistants. Large contexts and cross-cutting dependencies make generated code inconsistent. Clean Architecture spreads one feature across many layers.

## Decision

Organize code by feature (vertical slices) such as Features/Shelves/CreateShelf. Each slice holds its request, handler, validation, endpoint and tests. Do not use Clean Architecture layering.

## Consequences

- An assistant can implement a feature with only that folder as context.
- Less ceremony and fewer cross-feature dependencies.
- Shared concepts must live in the domain project; guard against duplicated models.
