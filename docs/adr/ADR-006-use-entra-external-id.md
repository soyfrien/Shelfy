# ADR-006: Use Microsoft Entra External ID for identity

- Status: Accepted (deferred: accounts use ASP.NET Core Identity first, see ADR-012; Entra can be added as an external login)
- Date: 2026-10-24

## Context

Shelfy needs consumer sign-up and sign-in without us storing passwords. The stack is Azure and .NET.

## Decision

Use Microsoft Entra External ID. Auth0 is the accepted alternative if Entra proves unsuitable. Internally represent users with our own User entity keyed by Guid, linked to the identity provider subject.

## Consequences

- No password storage; native Azure and ASP.NET Core integration.
- Our User table is decoupled from the provider so we can switch.
- Provider-specific setup must be isolated behind an interface.
