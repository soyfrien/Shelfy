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
- A visitor (and the owner on the home page) can find a book quickly: a sticky search box filters the cover grid by title or author as they type.

# Status (2026-10-25)

| Criterion | State |
|---|---|
| Unique shareable URL | Done: `/s/{shelfId}` (static template `wwwroot/shelf.html` plus `GET /api/shelves/{id}`) |
| Covers, titles, authors, no sign-in | Done. Title is the placeholder when a cover is missing or slow |
| Owner can turn sharing on or off | Search visibility is on by a "Show my shelf in search" switch (`PATCH /api/shelves/{id}`, account owners only). A private shelf is still readable by its link (guest shelves cannot be listed in search) |
| No availability, price or personal data | Done (the page shows covers, titles, authors only) |
| Mobile layout and link preview | Done: responsive grid, `og:title`, `og:description`, `og:image` (first cover) filled in server-side |
| Unknown shelf gives a not-found page | Done: 404 HTML page, also for hidden and deleted shelves |
| Owner can clear a shelf | Done: `POST /api/shelves/{id}/hide` (soft) and `DELETE /api/shelves/{id}` (hard) behind a "Clear shelf" button, no sign-in check yet (ADR-010) |
| Find a book quickly | Done: sticky "Search title or author" box on `/` and `/s/{id}` (client-side: case, accent and punctuation-insensitive, several words in any order, highlight, "N of M books", clear button, title/author sort, "No books match" state). The home page also searches the unmatched spine list (`GET /api/shelves/{id}/unmatched`) and the shelf page does not show or search it |
| Fix an unmatched spine | Done on the home page only (ADR-011): each entry in "Spines I couldn't match" is a 44 px tap target with a chevron; it opens a bottom sheet with the raw text, up to five suggested books (cover, title, author, tap to add), a search box (title, author or ISBN, 16 px so iOS does not zoom) and "Not a book / dismiss" (needs a second tap). A toast confirms ("Added ... to your shelf"). The list shows "N low-quality reads are hidden" with Show/Hide. Disabled while photos are still being read. The shared page `/s/{id}` has no fix controls and never shows unmatched spines |
