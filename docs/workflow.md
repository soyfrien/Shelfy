# AI prompt workflow

Each feature goes through the same sequence. Every prompt starts with `docs/system-prompt.md`, the ADRs and the feature requirement.

```text
Vision -> Requirement -> Architect -> Developer -> Reviewer -> Tester -> Documentation -> Merge
```

| Step | Role | Input | Output |
|---|---|---|---|
| 1 | Architect | requirement in `docs/requirements/` | `feature-spec.md` (domain objects, API endpoints, database schema, sequence diagram) |
| 2 | Developer | `feature-spec.md` | feature implementation |
| 3 | Reviewer | the code | `review.md` (security, performance, naming issues) |
| 4 | Tester | the code and spec | unit tests and integration tests |
| 5 | Documentation | the code | updated swagger/OpenAPI, markdown, ADRs |

## Prompt templates

**Architect:** Read the requirement. Produce domain objects, API endpoints, database schema and a sequence diagram. Write them to `feature-spec.md`. Follow all ADRs.

**Developer:** Implement the code from `feature-spec.md`. Follow all ADRs.

**Reviewer:** Review the code. Find security issues, performance issues and naming issues. Write `review.md`.

**Tester:** Generate unit tests and integration tests.

**Documentation:** Update swagger, markdown and ADRs.

## Conventions

- Put each feature's spec, review and tests next to its slice, for example `Features/Shelves/CreateShelf/`.
- Record any new architectural decision as an ADR before implementing it.
- Do not change frozen domain terms without a new ADR.
