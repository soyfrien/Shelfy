# Next: .NET MAUI app

Not scaffolded yet. The MAUI workloads (`dotnet workload install maui`, plus Xcode for iOS) are heavy, so the backend came first.

Plan, per [ADR-002](adr/ADR-002-use-dotnet-maui.md):

- Add `src/Shelfy.Mobile` (.NET MAUI, iOS and Android) to `src/Shelfy.sln`.
- First screens follow the v1 order in [vision.md](vision.md): take or pick a bookshelf photo, show upload and processing status, show the recognized shelf.
- Keep UI logic simple and testable; share DTO records with the API where it avoids duplicate models.
- Write a feature spec in `docs/requirements/` for each screen before implementing it.
