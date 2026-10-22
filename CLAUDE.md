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
| Unit tests | `dotnet test Sepiabox.slnx --filter "Category!=Integration&Category!=LiveApi"` |
| Integration tests (Docker, FFmpeg, models) | `dotnet test Sepiabox.slnx --filter "Category=Integration"` |
| Live API tests (Anthropic key, Docker; never in CI) | `dotnet test Sepiabox.slnx --filter "Category=LiveApi"` |
| Check formatting (what CI runs) | `dotnet format Sepiabox.slnx --verify-no-changes` |
| Fix formatting | `dotnet format Sepiabox.slnx` |
| Run the ingest CLI | `dotnet run --project src/Sepiabox.Ingest -- scan <folder>` |
| Add a catalog migration | `dotnet ef migrations add <Name> --project src/Sepiabox.Catalog --output-dir Migrations` |
| Run the local environment (needs Docker) | `dotnet run --project src/Sepiabox.AppHost` |
| Generate the Azure manifest and Bicep (offline) | `dotnet run --project src/Sepiabox.AppHost -- --publisher manifest --output-path <folder>` |
| Deploy to Azure (owner, needs azd and az) | `./scripts/deploy.sh` |

Before pushing, the build must have zero warnings (warnings are errors) and the format check must pass.

## Structure

```
src/
  Sepiabox.Pipeline/   processing stages, shared by the CLI and the cloud worker
  Sepiabox.Search/     pinned models and CLIP text encoder (ONNX Runtime only), used by the web and the pipeline
  Sepiabox.Storage/    IBlobStore and IBlobEventQueue, BlobLayout, the in-memory and Azure implementations (D-051, D-096); no media libraries
  Sepiabox.Ingest/     sepiabox-ingest console app (scan, process, upload, report)
  Sepiabox.Catalog/    EF Core catalog: CatalogDbContext, entities and migrations
  Sepiabox.Worker/     sepiabox-worker: migrate, import manifests into the catalog
  Sepiabox.Web/        Blazor web app (interactive server)
  Sepiabox.AppHost/    .NET Aspire AppHost: PostgreSQL, Azurite and the web, locally and for azd
  Sepiabox.ServiceDefaults/  health checks and OpenTelemetry shared by the services
infra/postgres/        PostgreSQL image with pgvector and PostGIS
scripts/deploy.sh      the only way to deploy: azd provision, migrate job, then the web (D-061)
azure.yaml             azd project: the AppHost is the infrastructure
tests/
  Sepiabox.<Project>.Tests/   one xUnit project per source project
  Sepiabox.Acceptance.Tests/  the MVP's end-to-end acceptance across ingest, worker and web (D-076)
  Shared/              helpers compiled into the test projects (Catalog/ holds the PostgreSQL + Azurite fixture, Places/ the GeoNames sample, Live/ the live-run recording rule)
docs/
  architecture.md, decisions.md, operations.md
  backlog/             one file per story plus the index
.github/workflows/ci.yml      lint (format, actionlint, ShellCheck), tests (Linux + Windows), integration (Linux), web-image (Linux: always on main, on PRs only when its inputs change)
.github/workflows/deploy.yml  deploy main to Azure after green CI or by hand: OIDC, production approval, scripts/deploy.sh
```

New projects follow the names in `docs/architecture.md` and are added to `Sepiabox.slnx`.

## Code conventions

- Docs, code, identifiers and comments in English. The app UI is English by default and localizable: every string the user sees comes from resource files through `IStringLocalizer`, with English and Brazilian Portuguese (`pt-BR`, never European Portuguese wording); routes are English (D-050).
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
- Tests that call a paid external API (the Anthropic golden run, S-025) are marked `[Trait("Category", "LiveApi")]`, skip without `SEPIABOX_ANTHROPIC_API_KEY` and never run in CI.
- Small binary fixtures live in `tests/<Project>.Tests/TestData/`; generate larger media in the test instead of committing it.

## Backlog workflow

The full cycle (story selection, the Atlas + Spock (+ DaVinci) review, priorities, iteration limit, CI, merge, blocked stories and the Agent Log) is in [docs/process.md](docs/process.md). In short:

1. Take the next `todo` story of your track (A or B from Sprint 13, D-090) in [docs/backlog/README.md](docs/backlog/README.md), in the order listed, whose dependencies are all `done`; finish the current sprint (both tracks) before starting the next (D-054, D-090).
   Two implementers, Tesla and Edison, work in parallel, each in its own worktree on one track; the PR is opened as a draft at iteration 1. Atlas and Spock review every story, and DaVinci also reviews stories that change the UI (process.md).
2. Work on a branch `story/S-XXX-short-slug`, one story per PR.
3. In that PR, set the story's status in the index (`doing` while open, `done` when the PR completes it, `blocked` with the reason when a human step from `docs/operations.md` is missing).
4. When something is not specified, choose the simplest conventional option and record it in `docs/decisions.md` (next D-number: date, decision, alternatives, reason).
