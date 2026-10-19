# CLAUDE.md

Guidance for agents working in `sepiabox-platform`. Read [docs/architecture.md](docs/architecture.md) first; decisions are in [docs/decisions.md](docs/decisions.md), human-only steps in [docs/operations.md](docs/operations.md) and the work queue in [docs/backlog/README.md](docs/backlog/README.md).

## Stack

- .NET 10 (C# latest), solution `Sepiabox.slnx`.
- Blazor (interactive server) for the web app, .NET Aspire for local development and `azd` deploys.
- PostgreSQL with pgvector and PostGIS through EF Core + Npgsql.
- MetadataExtractor, Magick.NET and external FFmpeg for media; ONNX Runtime for CLIP and faces.
- Azure (France Central): Container Apps, Blob Storage, Service Bus, Event Grid, Key Vault.

## Commands

| Task | Command |
|---|---|
| Build | `dotnet build Sepiabox.slnx` |
| Unit tests | `dotnet test Sepiabox.slnx --filter "Category!=Integration"` |
| Integration tests (Docker, FFmpeg, models) | `dotnet test Sepiabox.slnx --filter "Category=Integration"` |
| Check formatting (what CI runs) | `dotnet format Sepiabox.slnx --verify-no-changes` |
| Fix formatting | `dotnet format Sepiabox.slnx` |
| Run the ingest CLI | `dotnet run --project src/Sepiabox.Ingest -- scan <folder>` |
| Add a catalog migration | `dotnet ef migrations add <Name> --project src/Sepiabox.Catalog --output-dir Migrations` |
| Run the local environment (needs Docker) | `dotnet run --project src/Sepiabox.AppHost` |

Before pushing, the build must have zero warnings (warnings are errors) and the format check must pass.

## Structure

```
src/
  Sepiabox.Pipeline/   processing stages, shared by the CLI and the cloud worker
  Sepiabox.Ingest/     sepiabox-ingest console app (scan, process, upload)
  Sepiabox.Catalog/    EF Core catalog: CatalogDbContext, entities and migrations
  Sepiabox.Worker/     sepiabox-worker: migrate, import manifests into the catalog
  Sepiabox.Web/        Blazor web app (interactive server)
  Sepiabox.AppHost/    .NET Aspire AppHost: PostgreSQL, Azurite and the web, locally and for azd
  Sepiabox.ServiceDefaults/  health checks and OpenTelemetry shared by the services
infra/postgres/        PostgreSQL image with pgvector and PostGIS
tests/
  Sepiabox.<Project>.Tests/   one xUnit project per source project
docs/
  architecture.md, decisions.md, operations.md
  backlog/             one file per story plus the index
.github/workflows/ci.yml      lint, tests (Linux + Windows), integration (Linux)
```

New projects follow the names in `docs/architecture.md` and are added to `Sepiabox.slnx`.

## Code conventions

- Docs, code, identifiers and comments in English. Text the user sees in the app is in Portuguese.
- The product name is written `Sepiabox` in prose and namespaces (`Sepiabox.*`), `sepiabox` in lowercase identifiers such as executables, containers and URLs. Never `SepiaBox`, `Sepia Box` or `sepia-box`.
- Style is defined in `.editorconfig` and enforced in the build: file-scoped namespaces, braces always, `var` only when the type is evident.
- Comments explain why, not what. No commented-out code.
- No secrets in the repository: configuration comes from user secrets locally and Key Vault in Azure.
- The user's source files (photos, videos, drives) are only ever opened for reading.
- Dependencies must have an AGPL-compatible license (MIT, Apache-2.0, BSD). The code is AGPL-3.0; the Sepiabox name and logo are not covered by that license.
- ML models are never committed; they are downloaded with a pinned SHA-256.
- Catalog schema changes always add a new migration (`dotnet ef migrations add`); a migration that is on `main` is never edited or regenerated (D-033).
- Manifest blobs are only written through `ManifestStore.WriteMergedAsync`, never uploaded directly (D-036).

## Tests

- xUnit with `Assert`; bUnit for Blazor components; Testcontainers for PostgreSQL and Azurite.
- Test names follow `Does_something_when_condition`.
- Tests that need Docker, FFmpeg or ML models are marked `[Trait("Category", "Integration")]`; CI runs them on Linux only.
- Small binary fixtures live in `tests/<Project>.Tests/TestData/`; generate larger media in the test instead of committing it.

## Backlog workflow

1. Take the first `todo` story in [docs/backlog/README.md](docs/backlog/README.md) whose dependencies are all `done`.
2. Work on a branch `story/S-XXX-short-slug`, one story per PR.
3. In that PR, set the story's status in the index (`doing` while open, `done` when the PR completes it, `blocked` with the reason when a human step from `docs/operations.md` is missing).
4. When something is not specified, choose the simplest conventional option and record it in `docs/decisions.md` (next D-number: date, decision, alternatives, reason).
