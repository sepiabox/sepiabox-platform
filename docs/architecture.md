# Sepiabox architecture

Summary of the architecture chosen for the first version. Individual decisions, with alternatives and reasons, are in [decisions.md](decisions.md). Steps that need a person are in [operations.md](operations.md).

## The product

Sepiabox brings a family's photos and videos together (external drive first; after the MVP Google Drive, OneDrive and the exports of Google Photos, Apple Photos / iCloud, Instagram and Facebook; later, phones) in one place and finds any moment when you ask a question. Each file is interpreted **once, on arrival**; search only filters the stored metadata and never re-reads the photos.

Examples of questions the system must answer (the app UI is English by default, with Brazilian Portuguese as the first translation, D-050; the family asks in Portuguese):

- "Fotos minhas com a Ana e o Pedro juntos" (photos of me with Ana and Pedro together — people)
- "Fotos de Lisboa em dezembro", "O que fazia neste dia há cinco anos?" (photos of Lisbon in December, what was I doing on this day five years ago — time and place)
- "Fotos na neve", "Mais fotos parecidas com esta" (photos in the snow, more photos like this one — visual similarity)
- "Quantos casamentos fomos este ano?" (how many weddings did we go to this year — occasions)
- "Quantas fotos do HD já estavam no Google Drive?" (how many photos on the drive were already in Google Drive — organization)

Users in v1: **two people** (a couple), sharing **one library**. There is no open sign-up.

## Overview

```
PC with the drive ──sepiabox-ingest──► Blob Storage ──Event Grid──► Service Bus ──► Worker (Container Apps job) ──► PostgreSQL
 (processes locally)                    originals (Cold; none in index-only mode)     imports manifests                catalog + pgvector + PostGIS
                                        derivatives (Hot)
                                        manifests                                                                           ▲
                                                                                                                            │ SQL + kNN
Browser ──HTTPS──► Web (Blazor, Container Apps) ── Google/Microsoft sign-in + access list ──────────────────────────────────┘
                       │
                       └── question text ──► Claude Haiku 5.5 (Anthropic API) ──► filters (JSON DSL)
```

## Components (.NET 10)

| Project | Runs on | Responsibility |
|---|---|---|
| `Sepiabox.Search` | library | The pinned models (`models.json`, verification, ONNX sessions) and CLIP's text side: tokenizer and text encoder for search by description. ONNX Runtime only, so the web can use it (D-055, D-070). |
| `Sepiabox.Pipeline` | library | Processing stages: classification, hashing, metadata, thumbnails, video, embeddings, faces. Does not know where a file comes from. |
| `Sepiabox.Ingest` (`sepiabox-ingest`) | user's PC | `scan`, `process`, `upload`, `report` (unique assets in a work folder, S-062), `models download`. Processes the drive locally and uploads only what is new. |
| `Sepiabox.Catalog` | library | EF Core + Npgsql: schema, migrations, queries. |
| `Sepiabox.Storage` | library | The project's storage and queue interfaces (`IBlobStore`, `IBlobEventQueue`), `BlobLayout`, and their Azure implementations (D-051, from S-050). |
| `Sepiabox.Worker` (`sepiabox-worker`) | Container Apps job | Imports manifests, processes files that arrive through the cloud, groups events, syncs cloud sources. |
| `Sepiabox.Web` | Container Apps | Blazor (interactive server): sign-in, timeline, search, people, occasions, questions. |
| `Sepiabox.AppHost` / `Sepiabox.ServiceDefaults` | PC (dev) / azd | .NET Aspire: local environment (PostgreSQL + Azurite) and infrastructure description for `azd`. |

## Ingest flow

1. `sepiabox-ingest scan <folder>`: read-only; counts photos/videos, years, duplicates, unreadable files.
2. `sepiabox-ingest process <folder> --out <workdir>`: for each file computes the SHA-256 (asset identity), reads EXIF/QuickTime (date, GPS or a phone video's ISO 6709 location (D-060), camera, dimensions; an undated file takes its date from its file or parent folder name, D-046), generates WebP thumbnails, video posters and 720p versions, the CLIP embedding and, only with `--faces`, faces (D-047). Writes **one JSON manifest per asset** (schema v1, `docs/schemas/manifest.v1.json`). State is kept in SQLite so runs can resume.
3. `sepiabox-ingest upload <workdir>`: uploads originals (**Cold** tier), derivatives (**Hot**) and, last, the manifest. Resumes where it stopped. A work folder made by `process --index-only` (D-088, S-063) uploads no originals: they stay at their source (the drive, an export folder), its previews are larger (photos 2048 px, videos 1080p), and the catalog records where each original lives.
4. In the cloud, each new manifest triggers the import (Event Grid filtered to the `manifests` container → queue → worker job, one execution at a time).

Originals are **never re-read** in the cloud during local ingest (reading the Cold tier costs ~US$ 0.03/GB). Files that arrive through the cloud (Drive, OneDrive) go through the same pipeline in the worker, in reference mode (D-023): their originals stay in Drive or OneDrive. Exports of Google Photos, Apple Photos / iCloud, Instagram and Facebook are extracted on the PC and read by `process --export <kind>`, which takes dates, places, captions and albums from the export's metadata files (D-053).

## Data

- **Blob Storage**: `originals` (Cold; only for sources not in index-only or reference mode, D-023, D-088), `derivatives` (Hot), `manifests`, `incoming`, `exports`. Public access disabled; the browser only reads through short-lived SAS URLs issued after sign-in.
- **PostgreSQL Flexible (B1ms)** with **pgvector** (512-d CLIP and 128-d face embeddings, HNSW indexes) and **PostGIS** (location, distance, places). Main tables: `assets`, `asset_sources`, `derivatives`, `asset_embeddings`, `faces`, `people`, `settings`, `events`, `event_assets`, `saved_places`, `geonames`.
- **Place names**: GeoNames `cities500` loaded into PostgreSQL (offline, no Azure Maps). Personal places ("Beach house", and one place flagged as home, S-014) are points with a radius saved by the user.

## ML models (ONNX Runtime, CPU only, D-042)

- **CLIP ViT-B/32** (MIT): image embedding (search by description, "more like this", occasion types by zero-shot).
- **YuNet** (face detection, MIT) + **SFace** (face embedding, Apache-2.0), from the OpenCV Zoo. Faces are **opt-in**.
- Models are not stored in the repository: `sepiabox-ingest models download` fetches them with SHA-256 pinned in `models/models.json`.

## Search

All search goes through a **JSON DSL** (`docs/schemas/search.v1.json`): filters for time, place, people, source, kind, occasion, visual similarity and aggregations. The filter UI and the question translator produce the same DSL; a single service executes it (SQL + kNN in pgvector).

Natural-language questions: the text (never the photos) goes to **Claude Haiku 5.5** (`claude-haiku-5-5`) through the **Anthropic API** (D-052), with *structured outputs* restricted to the DSL schema. Visual terms are translated to English (CLIP is English-only). Daily question limit per user.

## Access and privacy

- Sign-in with **Google** and **Microsoft** (Apple will come with the iOS app). A **list of allowed e-mails** is checked when the sign-in returns (not on the list, no cookie) and a default authorization policy protects every route.
- In Azure each service has its own managed identity with only the roles it uses; the web connects to PostgreSQL as a role that cannot change the schema, and reads the owner's secrets (sign-in, the access list, the Anthropic key) from Key Vault; its data protection keys live in Blob Storage under a Key Vault key (D-062).
- Face recognition is optional; it can be turned off and face data deleted. No inference of sensitive categories (religion, health).
- Full export at any time.

## Cloud and costs

- Azure, **France Central** region, resource group `rg-sepiabox`. Ceiling: **€50/month** for Azure (D-008); estimate ~US$ 30–35/month with 1 TB. The Anthropic API (questions, D-052) is billed separately by Anthropic under its own spend limit, outside that ceiling.
- Container Apps (web scales to zero; worker as a job), PostgreSQL B1ms, Storage, Service Bus Basic, Event Grid, Key Vault, Application Insights with a daily cap, Container Registry Basic.
- Safeguards (repository `sepiabox-infra`): €50 budget with alerts at €25/€40/forecast, anomaly alerts, automatic shutdown at €45, Azure Policy of allowed resource types, log cap.
- Deploy: `azd` from the AppHost through `scripts/deploy.sh`, which runs the `sepiabox-migrate` Container Apps job before the web's new revision (D-061); GitHub Actions with OIDC (no secrets in the repository).
- **Portability (D-051):** an architecture principle that must not slow the MVP. Blob storage, queue and events, secrets and the LLM sit behind the project's own small interfaces, Azure (and the Anthropic API for Claude) being one implementation wired in the composition roots; catalog access stays behind EF Core and the project's query services, with pgvector/PostGIS queries in provider-specific classes, so PostgreSQL could give way to another relational database (for example SQL Server) and Azure to another cloud (for example AWS). It applies to new code now; existing code was refactored after the MVP (S-050, S-051): the Azure Blob SDK is now only in `Sepiabox.Storage` and the composition roots. No alternative implementation is built yet.

## Repositories (organization `sepiabox`)

| Repository | Contents |
|---|---|
| `sepiabox-platform` | This repository: .NET code, docs, backlog |
| `sepiabox-infra` | Budget, alerts, shutdown, Policy (Bicep) |
| `sepiabox-site` | Static site `sepiabox.com` (GitHub Pages) |
| `sepiabox-brand` | Logo, colors, typography (all rights reserved) |
| `sepiabox-planning` | Private: business plan |

## Visual identity (for the UI)

Colors: Sepia `#9A5A2C` (brand), Cyanotype `#1F4C74` (actions), Developer `#231A14` (text), Silver paper `#EEECE7` (background), Mat `#D8D1C6` (lines). Supporting tones: Cyanotype hover `#163A59`, surface `#F8F7F4`, secondary text `#5C5048`, filter chips `#E6EBF0`. Typefaces: Bricolage Grotesque (headings, 500/700/800), Figtree (body, 400/500/600), IBM Plex Mono (filters, dates and metadata). The wordmark is lowercase "sepiabox" in Bricolage Grotesque 800 with letter spacing −0.045 em, "sepia" in Sepia and "box" in Developer (on dark backgrounds `#D59B67` and `#EEECE7`). Symbol in `sepiabox-brand/assets/`; the favicon is its simplified `symbol-small.svg`. The app UI is in **English** by default and localizable, with **Brazilian Portuguese** (`pt-BR`) as the first translation: user-facing text lives in resource files, the culture comes from the browser or an explicit switch (D-050). Code, comments, docs and technical names are in **English**. The web app targets **desktop** (minimum width ~1024 px); on phones it only needs to open without breaking.

## Phases

- **MVP** (Sprints 1–9, D-053, D-054): take the data from the external drive (the only source) to the cloud, ask questions about the imported photos and videos and get correct answers: local processing with embeddings and opt-in faces, upload and automatic import, timeline, restricted sign-in, English and Brazilian Portuguese UI, place names (with phone video locations) and filters, search by description (the CLIP text model shipped with the web, D-055), natural-language questions and counting answers checked against a golden set, deployed to Azure with cost safeguards, and an end-to-end acceptance test from a generated folder to the answers, in CI, plus the owner's check of a few counts on the real data (D-056).
- **Phase 2** (after the MVP, Sprints 10–16; from Sprint 13 each sprint has two parallel tracks, D-090): people, undoing a wrong upload (S-057), personal places and "more like this", occasions and types, organization (blurry photos, bursts, storage), the portability refactor of existing code (D-051), Google Drive and OneDrive, full export, region search, index-only mode for any source (D-088), and the official exports of Google Photos (Takeout), Apple Photos / iCloud, Instagram and Facebook processed locally by `sepiabox-ingest process --export` (D-053); later, a local UI for pre-processing.
- **Phase 3 (outside this backlog)**: relationship graph between people, captions from a vision model, mobile apps, dedicated object/clothing detection, OCR and transcription.
