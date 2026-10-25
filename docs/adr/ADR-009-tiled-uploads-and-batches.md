# ADR-009: Wide shelf photos are cut into overlapping tiles, grouped by a client-chosen batch id

- Status: Accepted
- Date: 2026-10-25

## Context

A photo of a long shelf (or a panorama) has many small spines. Azure AI Vision reads at most about 4 megapixels well, and the
browser used to downscale everything to 3200 px on the long side, so on a wide shelf the spine text became too small and
books were missed. Reading a big photo is also slow, and the user saw one spinner for the whole time.

## Decision

1. **The browser tiles.** A photo that is wide or large is cut along its long axis into overlapping tiles (about 22% overlap)
   with a canvas, each tile encoded as JPEG (long side at most 2400 px). Normal photos are still uploaded as one image.
   The tile plan is a pure function (`wwwroot/tiles.js`, `planTiles`), tested with node (`tools/test-tiles.js`). The server
   does no image processing: no image library on a 1 GB VM, and the browser already decodes HEIC and fixes rotation.
2. **A batch is just a shared id.** The existing upload endpoint accepts three optional form fields, `batchId` (a GUID chosen
   by the client), `part` and `parts`. They are stored on `RecognitionJob` (`BatchId`, `TileIndex`, `TileCount`).
   There is no "create batch" call and no batch table. Old clients that send no batch fields behave exactly as before.
   A new `GET /api/shelves/{shelfId}/batches/{batchId}` returns the progress per part and the merged result, so the UI polls one
   endpoint instead of one per tile. A tile that failed is re-uploaded with the same batch fields; it becomes a new job and the
   batch status reports the newest job per part.
3. **De-duplication happens on the server, when a tile is processed.**
   - Books resolved by ISBN-13 are upserted once and `Shelf.AddBook` never adds a book twice, so the same book read in two
     tiles gives one `ShelfBook`.
   - Unresolved reads are compared with the reads of the other tiles of the same batch (`SpineDeduper`, pure and unit-tested):
     a read that repeats a peer is dropped, a better (longer, more confident) read replaces a fragment, and an unresolved
     read explained by a resolved book (title or text) is dropped. Reads from tiles that are mostly noise (fewer than four
     letters, or mean confidence below 0.35) are not listed.
   - Texts are only ever compared to remove *unresolved* duplicates and fragments. Two resolved books are never merged by text.
4. **Rate limiting is in the OCR client.** A process-wide `OcrThrottle` spaces OCR calls (default 3.2 s, about 18 per minute, under
   the free tier's 20). A 429 is retried in place after `Retry-After` (at most 30 s, twice) when the photo stream can be
   rewound; after that the job queue's backoff takes over, as before. The tiles can therefore all be uploaded at once.

## Consequences

- A 6000 px wide shelf becomes about four tiles of 1800 px and takes about four OCR calls (roughly 15 s in total).
  The free tier (5000 calls a month) allows about 1200 such shelves.
- Only unresolved reads rely on fuzzy text matching. A known weakness: a one-word fragment of a spine ("Foundation" from a
  spine read in full as "Foundation | Isaac Asimov" in the next tile) is not merged unless the word is visibly cut, because
  merging single exact words would also merge neighbouring titles of a series ("Dune" and "Dune Messiah"). Such a fragment is
  only listed in the "couldn't match" list; it is not added to the shelf unless it resolves, and then by ISBN.
- No new table; one migration adds three columns and an index to `RecognitionJobs`.
- Tiles are stored like any other photo (about 1 MB each).
- Server-side tiling (for the MAUI app or API clients) can reuse the same batch fields later; `tiles.js` documents the plan.
