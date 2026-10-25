# ADR-003: Use ASP.NET Core 8 Web API

- Status: Accepted
- Date: 2026-10-24

## Context

We need a fast, mature backend with good OpenAPI support and easy Azure deployment.

## Decision

Use ASP.NET Core 8 Web API with minimal APIs or thin controllers. No business logic in controllers or endpoints.

## Consequences

- Fast and well documented.
- Easy deployment to Azure App Service.
- OpenAPI/Swagger generated for clients and documentation.
