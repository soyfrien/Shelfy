# ADR-011: Tap to fix unmatched spines, and remember the fixes

- Status: Accepted
- Date: 2026-10-25

## Context

Recognition is imperfect. On a real 56-book shelf (230 spines read from two photos) 57 spines matched a book and 173 stayed
as raw text in "Spines I couldn't match", with nothing the user could do about them (ADR-007 named manual correction as future work).
Real titles are often garbled by OCR, printed in unusual lettering, or missing from the catalogs, so a person is the best judge.
Corrections are also valuable twice: the same book photographed again (or by someone else) reads the same way.
There is still no sign-in (ADR-006), so everything below is open, like the rest of the API (see ADR-010 for the trust model).

## Decision

1. **Tap to fix.** Each unmatched spine on the home page is tappable and opens a bottom sheet: the text that was read, up to five
   suggested books (cover, title, author), a search field (title, author or ISBN) and "Not a book / dismiss".
   Picking a book adds it to the shelf; dismissing hides the spine.
2. **Suggestions** (`GET /api/shelves/{id}/spines/{spineId}/suggestions`) try the spine text several ways through a new
   `IBookSearch` (same providers and order as the resolver: OpenLibrary, then Google Books, merged by ISBN): the whole text, the
   parsed title and author, the longest line alone, title and author swapped, then single lines. At most six lookups, stopping early
   once five good candidates exist. Each candidate is scored against the text that found it (`MatchScorer.TitleSimilarity` over
   title, title+author and author+title); candidates below 0.3 are dropped and the best five returned. With `?q=` the same endpoint
   is the sheet's search box: books Shelfy already knows first, then a free-text catalog search (an ISBN is looked up as an ISBN).
   Nothing is stored. If the catalogs fail the answer is 200 with `unavailable: true`, never an error.
3. **Resolve** (`POST .../spines/{spineId}/resolve`, body `isbn13` or `bookId`, plus `title`/`author`/`coverUrl` for a book that is new here):
   upserts the `Book` by ISBN-13, adds it to the shelf as Private (never twice), marks the spine, and every other unresolved read of the same
   text on that shelf, as resolved with source `User` and score 1, and stores the correction. Idempotent: the same book again answers 200 with
   nothing added; a different book for an already matched spine is 409. Client-sent details are only used when the book is new (an existing
   `Books` row is never changed through this endpoint). Hidden or unknown shelves and spines of other shelves are 404.
4. **Dismiss** (`POST .../spines/{spineId}/dismiss`): sets `DetectedSpines.Dismissed` on the spine and its same-text repeats; they stay in
   the database and are no longer listed (the list endpoint, the batch endpoint). Idempotent; 409 for an already matched spine; 404 as above.
   Nothing is remembered. The UI asks for a second tap.
5. **Remembered corrections.** Table `UserCorrections` (`TextKey` unique, `BookId`, `UseCount`, timestamps), global (no accounts yet).
   The key is the spine's text normalized like `SpineDeduper.Tokens`: lowercase, accents and punctuation removed, one space between words,
   at most 500 characters, and at least four letters (shorter texts are too ambiguous to remember). Two keys are stored per fix: the whole raw
   text, and the parsed title when it is two words or a long single word. The pipeline loads the corrections for a photo's spines in one query and,
   for each spine, uses a remembered book before calling any catalog (match score 1, source `Memory`, `UseCount` incremented). Fixing a text again
   with another book replaces the old one (one row per key). Corrections are never deleted by the API; an operator can `DELETE` a row.
6. **Noise filter** (`SpineNoiseFilter`, applied when listing, never when storing): reads that are too short (fewer than 3 letters, or a lone word
   under 4 letters), mostly punctuation or digits, only filler words (the, of, with...), or short and unsure (confidence under 0.5 with fewer than
   12 letters, a lone word under 0.6, one or two words under 0.3), and cut-off or noisier repeats of another listed read, are not listed. The UI
   says "N low-quality reads are hidden" with a Show button (`?includeHidden=true` returns them flagged with a reason). The thresholds were
   chosen on the 173 unmatched reads of a real shelf, erring towards showing (see docs/recognition.md); every real title there stayed listed.
7. **Schema:** migration `AddUserCorrections` adds `DetectedSpines.Dismissed boolean NOT NULL DEFAULT false` and the `UserCorrections` table.
   Additive: the previous build keeps working, so the migration is applied before the new build.

## Consequences

- Until sign-in exists, anyone holding a shelf id can fix or dismiss its spines, and a bad fix is remembered for everyone: a vandal could make a
  text resolve to the wrong book on other shelves. Accepted for the test phase for the same reason as ADR-010; the endpoints carry
  `TODO(ADR-006)`. Mitigations in place: corrections are keyed by long, specific text; an existing `Books` row is never edited through the API;
  rows are easy to inspect and delete. When accounts arrive, corrections should become per user (and optionally promoted to global after N agreeing users).
- A remembered correction matches on the exact normalized text. A different OCR misreading of the same spine will still need its own fix (each
  fix also teaches the parsed-title key, which helps when only the surrounding noise differs).
- A text fixed once is resolved on that shelf immediately, but unmatched spines on other shelves are not touched retroactively; they match
  when photographed again.
- Dismissed spines are not remembered: the same text in a later photo is listed again.
- Suggestions cost up to six catalog calls per tap, only when a person opens the sheet.
