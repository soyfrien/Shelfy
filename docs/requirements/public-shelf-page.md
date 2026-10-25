# Feature

Public Shelf Page

# User Story

As a reader, I want a shareable page for my shelf so that friends can see what I read.

# Acceptance Criteria

- Each shelf has a unique, shareable URL.
- The page shows book covers, titles and authors, and is readable without signing in.
- The owner can turn public sharing on or off; it is off by default.
- Book availability, price and personal data are not shown in v1.
- The page renders well on mobile and has link-preview metadata (title, image).
- A disabled, hidden, deleted or unknown shelf URL returns a not-found page.
- The owner can clear a shelf: hide it (kept, not shown) or delete it with its photos (ADR-010).

# Status (2026-10-25)

| Criterion | State |
|---|---|
| Unique shareable URL | Done: `/s/{shelfId}` (static template `wwwroot/shelf.html` plus `GET /api/shelves/{id}`) |
| Covers, titles, authors, no sign-in | Done. Title is the placeholder when a cover is missing or slow |
| Owner can turn sharing on or off | Not built. `Shelf.IsPublic` exists but is not enforced: every shelf is readable by its link until authentication (ADR-006) exists |
| No availability, price or personal data | Done (the page shows covers, titles, authors only) |
| Mobile layout and link preview | Done: responsive grid, `og:title`, `og:description`, `og:image` (first cover) filled in server-side |
| Unknown shelf gives a not-found page | Done: 404 HTML page, also for hidden and deleted shelves |
| Owner can clear a shelf | Done: `POST /api/shelves/{id}/hide` (soft) and `DELETE /api/shelves/{id}` (hard) behind a "Clear shelf" button, no sign-in check yet (ADR-010) |

