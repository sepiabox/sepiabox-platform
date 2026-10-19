# Sepiabox architecture

Summary of the architecture chosen for the first version. Individual decisions, with alternatives and reasons, are in [decisions.md](decisions.md). Steps that need a person are in [operations.md](operations.md).

## The product

Sepiabox brings a family's photos and videos together (external drive, Google Drive, OneDrive and, later, phones) in one place and finds any moment when you ask a question. Each file is interpreted **once, on arrival**; search only filters the stored metadata and never re-reads the photos.

Examples of questions the system must answer (the app UI is in Portuguese):

- "Fotos minhas com a Ana e o Pedro juntos" (photos of me with Ana and Pedro together — people)
- "Fotos de Lisboa em dezembro", "O que fazia neste dia há cinco anos?" (photos of Lisbon in December, what was I doing on this day five years ago — time and place)
- "Fotos na neve", "Mais fotos parecidas com esta" (photos in the snow, more photos like this one — visual similarity)
- "Quantos casamentos fomos este ano?" (how many weddings did we go to this year — occasions)
- "Quantas fotos do HD já estavam no Google Drive?" (how many photos on the drive were already in Google Drive — organization)

Users in v1: **two people** (a couple), sharing **one library**. There is no open sign-up.

## Overview

```
PC with the drive ──sepiabox-ingest──► Blob Storage ──Event Grid──► Service Bus ──► Worker (Container Apps job) ──► PostgreSQL
 (processes locally)                    originals (Cold)                              imports manifests                catalog + pgvector + PostGIS
                                        derivatives (Hot)
                                        manifests                                                                           ▲
                                                                                                                            │ SQL + kNN
Browser ──HTTPS──► Web (Blazor, Container Apps) ── Google/Microsoft sign-in + access list ──────────────────────────────────┘
                       │
                       └── question text ──► Claude Haiku 5.5 (Microsoft Foundry) ──► filters (JSON DSL)
```

## Components (.NET 10)

| Project | Runs on | Responsibility |
|---|---|---|
| `Sepiabox.Pipeline` | library | Processing stages: classification, hashing, metadata, thumbnails, video, embeddings, faces. Does not know where a file comes from. |
| `Sepiabox.Ingest` (`sepiabox-ingest`) | user's PC | `scan`, `process`, `upload`, `models download`. Processes the drive locally and uploads only what is new. |
| `Sepiabox.Catalog` | library | EF Core + Npgsql: schema, migrations, queries. |
| `Sepiabox.Worker` (`sepiabox-worker`) | Container Apps job | Imports manifests, processes files that arrive through the cloud, groups events, syncs cloud sources. |
| `Sepiabox.Web` | Container Apps | Blazor (interactive server): sign-in, timeline, search, people, occasions, questions. |
| `Sepiabox.AppHost` / `Sepiabox.ServiceDefaults` | PC (dev) / azd | .NET Aspire: local environment (PostgreSQL + Azurite) and infrastructure description for `azd`. |

## Ingest flow

1. `sepiabox-ingest scan <folder>`: read-only; counts photos/videos, years, duplicates, unreadable files.
2. `sepiabox-ingest process <folder> --out <workdir>`: for each file computes the SHA-256 (asset identity), reads EXIF/QuickTime (date, GPS, camera, dimensions), generates WebP thumbnails, video posters and 720p versions, the CLIP embedding and, if enabled, faces. Writes **one JSON manifest per asset** (schema v1, `docs/schemas/manifest.v1.json`). State is kept in SQLite so runs can resume.
3. `sepiabox-ingest upload <workdir>`: uploads originals (**Cold** tier), derivatives (**Hot**) and, last, the manifest. Resumes where it stopped.
4. In the cloud, each new manifest triggers the import (Event Grid filtered to the `manifests` container → queue → worker job, one execution at a time).

Originals are **never re-read** in the cloud during local ingest (reading the Cold tier costs ~US$ 0.03/GB). Files that arrive through the cloud (Drive, OneDrive) go through the same pipeline in the worker.

## Data

- **Blob Storage**: `originals` (Cold), `derivatives` (Hot), `manifests`, `incoming`, `exports`. Public access disabled; the browser only reads through short-lived SAS URLs issued after sign-in.
- **PostgreSQL Flexible (B1ms)** with **pgvector** (512-d CLIP and 128-d face embeddings, HNSW indexes) and **PostGIS** (location, distance, places). Main tables: `assets`, `asset_sources`, `derivatives`, `asset_embeddings`, `faces`, `people`, `events`, `saved_places`, `geonames`.
- **Place names**: GeoNames `cities500` loaded into PostgreSQL (offline, no Azure Maps). Personal places ("Casa", "Casa da praia") are points with a radius saved by the user.

## ML models (ONNX Runtime, CPU by default, optional CUDA)

- **CLIP ViT-B/32** (MIT): image embedding (search by description, "more like this", occasion types by zero-shot).
- **YuNet** (face detection, MIT) + **SFace** (face embedding, Apache-2.0), from the OpenCV Zoo. Faces are **opt-in**.
- Models are not stored in the repository: `sepiabox-ingest models download` fetches them with SHA-256 pinned in `models/models.json`.

## Search

All search goes through a **JSON DSL** (`docs/schemas/search.v1.json`): filters for time, place, people, source, kind, occasion, visual similarity and aggregations. The filter UI and the question translator produce the same DSL; a single service executes it (SQL + kNN in pgvector).

Natural-language questions: the text (never the photos) goes to **Claude Haiku 5.5** (`claude-haiku-5-5`) on **Microsoft Foundry**, with *structured outputs* restricted to the DSL schema. Visual terms are translated to English (CLIP is English-only). Daily question limit per user.

## Access and privacy

- Sign-in with **Google** and **Microsoft** (Apple will come with the iOS app). A **list of allowed e-mails** is checked when the sign-in returns (not on the list, no cookie) and a default authorization policy protects every route.
- Face recognition is optional; it can be turned off and face data deleted. No inference of sensitive categories (religion, health).
- Full export at any time.

## Cloud and costs

- Azure, **France Central** region, resource group `rg-sepiabox`. Ceiling: **€50/month**; estimate ~US$ 30–35/month with 1 TB.
- Container Apps (web scales to zero; worker as a job), PostgreSQL B1ms, Storage, Service Bus Basic, Event Grid, Key Vault, Application Insights with a daily cap, Container Registry Basic.
- Safeguards (repository `sepiabox-infra`): €50 budget with alerts at €25/€40/forecast, anomaly alerts, automatic shutdown at €45, Azure Policy of allowed resource types, log cap.
- Deploy: `azd` from the AppHost; GitHub Actions with OIDC (no secrets in the repository).

## Repositories (organization `sepiabox`)

| Repository | Contents |
|---|---|
| `sepiabox-platform` | This repository: .NET code, docs, backlog |
| `sepiabox-infra` | Budget, alerts, shutdown, Policy (Bicep) |
| `sepiabox-site` | Static site `sepiabox.com` (GitHub Pages) |
| `sepiabox-brand` | Logo, colors, typography (all rights reserved) |
| `sepiabox-planning` | Private: business plan |

## Visual identity (for the UI)

Colors: Sepia `#9A5A2C` (brand), Cyanotype `#1F4C74` (actions), Developer `#231A14` (text), Silver paper `#EEECE7` (background), Mat `#D8D1C6` (lines). Typefaces: Bricolage Grotesque (headings), Figtree (body), IBM Plex Mono (filters and metadata). Symbol in `sepiabox-brand/assets/`. The app UI is in **Portuguese**; code, comments, docs and technical names are in **English**. The web app targets **desktop** (minimum width ~1024 px); on phones it only needs to open without breaking.

## Phases

- **MVP**: local drive → cloud, timeline, date/place filters, visual search, people, restricted sign-in, deploy with cost safeguards.
- **Phase 2**: occasions and types, natural-language questions, Google Drive and OneDrive, organization (blurry photos, bursts), export.
- **Phase 3 (outside this backlog)**: relationship graph between people, captions from a vision model, mobile apps, dedicated object/clothing detection, OCR and transcription.
