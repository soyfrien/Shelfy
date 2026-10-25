# Vision

## Idea

A reader takes a photo of their bookshelf. Shelfy identifies every book from its spine and builds a structured, beautiful digital shelf.

## Long-term

Users become nodes in a social "library fabric". Each book is marked Private, Loan or Sale. Trading, recommendations and social discovery sit on top of the bookshelf graph. Example queries: "Show me Sci-Fi near Brooklyn", "Who owns Foundation?", "Books by Neal Stephenson."

## Guiding principle

Do not build the social graph first. Build, in this order:

```text
Photo -> Recognized Shelf -> Beautiful Shelf Profile
```

If the scan feels magical, the social layer has something to stand on.

## Roadmap

1. **v1 (MVP):** MAUI app, photo upload, OCR and metadata resolution, digital bookshelf, shareable public shelf page. No trading, messaging or social graph.
2. **v2:** Availability (Private / Loan / Sale) visible to others, discovery and search across shelves.
3. **v3:** Trades, messaging, recommendations (collaborative filtering, then AI).

## Success for v1

- A user can scan a shelf and get mostly correct books with minimal manual correction.
- The public shelf page is good enough that users share it.
