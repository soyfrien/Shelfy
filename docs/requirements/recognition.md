# Feature

Book Recognition

# User Story

As a reader, I want Shelfy to identify the books in my shelf photo so that I do not have to type them in.

# Acceptance Criteria

- A background worker picks up each queued photo.
- OCR (Azure AI Vision) extracts text from book spines.
- Extracted text is resolved against Google Books and OpenLibrary to an ISBN, title, author and cover.
- Resolved books are attached to the user's Shelf as ShelfBook with status Private.
- Spines that cannot be resolved are listed as unmatched so the user can correct them manually.
- Tapping an unmatched spine offers suggested books and a search; the chosen book is added to the shelf and the fix is remembered, so the same spine text matches automatically next time.
- Very poor reads (too short, mostly punctuation, filler words, very unsure, repeats) are not listed as unmatched, but are counted and can be shown.
- The same book is not added twice from one photo.
- Processing status (queued, processing, complete, failed) is visible to the user.
- Failures are retried and then dead-lettered without losing the photo.
- A wide or large photo is read in overlapping parts, with visible progress per part; a book or spine seen in two parts appears once.

# Status (2026-10-25)

| Criterion | State |
|---|---|
| Background worker picks up each queued photo | Done (`RecognitionWorker`, database queue) |
| OCR extracts text from spines | Done with Azure AI Vision Read client; not yet run against the real service |
| Resolve against Google Books and OpenLibrary | Done, OpenLibrary first, Google Books fallback |
| Attach resolved books as Private ShelfBook | Done |
| Unresolved spines listed as unmatched | Done: kept as `DetectedSpine` rows with status Unresolved |
| Tap to fix | Done (ADR-011): suggestions (`GET .../spines/{id}/suggestions`), pick (`POST .../resolve`), search box (`?q=`), "Not a book" (`POST .../dismiss`), in the home page's bottom sheet |
| Remembered corrections | Done (ADR-011): `UserCorrections` (normalized text key to book), consulted by the worker before the catalogs, global until accounts exist |
| Noise filter on the unmatched list | Done (ADR-011): `SpineNoiseFilter`; see the numbers below |
| Same book not added twice from one photo | Done |
| Status visible (queued, processing, complete, failed) | Done: Pending, Processing, Completed, Failed |
| Failures retried then dead-lettered without losing the photo | Done: backoff retries, then Failed; photo kept |
| Wide photos read in overlapping parts, progress, no duplicates | Done in the web UI and API (ADR-009): tiles, batch status endpoint, de-duplication of unresolved spines, OCR throttle and 429 retry. Verified on a synthetic 16-spine panorama; real phone photos untested |

Accuracy on real shelf photos is unmeasured. See [pipeline.md](../pipeline.md).

## Noise filter: tuning on a real shelf (2026-10-25)

The thresholds were chosen offline on the 230 spines read from the two photos of a real 56-book shelf (read-only copy of the spine text and OCR
confidence; 57 spines matched a book, 173 did not). Many of those are language dictionaries and grammars, with fragments of long titles.

| | Count |
|---|---|
| Unmatched reads | 173 |
| Listed before (distinct text, ignoring case) | 171 |
| Listed now | 146 |
| Hidden as low quality | 23 (12 unsure, 4 too short, 4 repeats or cut-off repeats, 3 filler) |

What was hidden: `TH`, `HEN`, `OPE`, `FAN`, `THE`, `WITH`, `WHO`, low-confidence one-word reads such as `putnam` (0.22), `nelish`, `TALE`, `AMERTON`,
short unsure ones such as `OGET'S TI` and `GENE NIE`, and repeats such as `WILLIAM | SAFIRE` next to `William Safire In Love with Norma Loqu , E`
and `DICTIONARY` next to `DICTIONA | DICTIONARY`. What stays on purpose: any read that looks like words, including single words (`SPANISH`, `Rice`,
`NORWEGIAN`), since a real one-word title (Dune, Emma) must never disappear. The honest limit: most of the remaining 146 are real but incomplete
titles that OCR cut or garbled, and a spelling-aware filter would need a dictionary; the answer to those is tap to fix, not hiding. The hidden
reads are one tap away ("N low-quality reads are hidden" - Show).
