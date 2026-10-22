# Sepiabox backlog

Backlog meant to be executed by autonomous agents. Each story is a small vertical slice (up to ~400 changed lines), depends only on earlier stories and carries all the context it needs. Sources: the product design conversation and [`docs/`](..). Decisions taken by default are in [`docs/decisions.md`](../decisions.md).

## How to use

1. Pick the first `todo` story in the order below whose dependencies are all `done`, finishing the current sprint before starting the next.
2. Set its status to `doing` in this table, in the same PR as the implementation.
3. Implement until every acceptance criterion and expected test passes and CI is green.
4. Set the status to `done` in the PR. If a human step is missing ([`docs/operations.md`](../operations.md)), set `blocked` and say which.

Statuses: `todo` · `doing` · `blocked` · `done`.

**Sprints** ([D-054](../decisions.md)): a sprint is finished completely before the next one starts, and stories are taken in the order below, within the current sprint; there are no order overrides. A `todo` story whose dependency is `blocked` becomes `blocked` ("depends on S-XXX"). A story whose only open items are manual owner checks is `done` once its automated criteria pass; the manual checks go to [`operations.md`](../operations.md) as owner acceptance. Sprints 3–12 fit in about 4 hours of agent work including the Atlas + Spock reviews (2 stories M + M, or up to 3 when some are S). From Sprint 13 the team is two implementers working in parallel ([D-090](../decisions.md)): each sprint is twice that size, as two independent **tracks** (A and B) of 2–3 stories, about 4 hours each, one implementer per track; the sprint is finished when both tracks are. Within a track, stories are taken in the order listed. Sprints 1–2 were re-cut from the work already done.

**MVP** ([D-053](../decisions.md)): Sprints 1–9. Take the data from the external drive to the cloud, ask questions about the imported photos and videos, and get correct answers. The only source in the MVP is the external drive (local folders and drives). Sprints 10–16 come after the MVP and do not start while an MVP story is `blocked`.

**UI text** ([D-050](../decisions.md)): from S-044 on, every story writes the text the user sees as resources in English and Brazilian Portuguese, and uses English routes. Portuguese strings quoted in stories are the `pt-BR` translation.

## Execution order

### Sprint 1 — Local processing and cost guards (done)

**Demonstrable increment:** `sepiabox-ingest scan` and `process` read a real drive into manifests, thumbnails, 720p versions and dates for undated files; `dotnet run --project src/Sepiabox.AppHost` starts PostgreSQL and Azurite; the Azure budget, alerts, automatic shutdown and resource policy are in place; `sepiabox.com` is published.

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-001 | [Set up the repository foundation](S-001-set-up-the-repository-foundation.md) | Foundation | — | High | M | `done` |
| S-002 | [Scan a drive and report what it contains](S-002-scan-a-drive-and-report-what-it.md) | Local ingest | S-001 | High | M | `done` |
| S-003 | [Start the local environment with .NET Aspire](S-003-start-the-local-environment-with-net-aspire.md) | Foundation | S-001 | High | M | `done` |
| S-004 | [Describe each photo and video in a manifest](S-004-describe-each-photo-and-video-in-a.md) | Local ingest | S-002 | High | M | `done` |
| S-005 | [Generate photo thumbnails](S-005-generate-photo-thumbnails.md) | Local ingest | S-004 | High | S | `done` |
| S-006 | [Generate video posters and 720p versions](S-006-generate-video-posters-and-720p-versions.md) | Local ingest | S-004 | High | M | `done` |
| S-043 | [Date files from folder and file names](S-043-date-files-from-folder-and-file-names.md) | Local ingest | S-004 | High | M | `done` |
| S-029 | [Protect the budget with alerts](S-029-protect-the-budget-with-alerts.md) | Cloud and costs | — | High | S | `done` |
| S-030 | [Shut everything down automatically at €45](S-030-shut-everything-down-automatically-at-45.md) | Cloud and costs | S-029 | High | M | `done` |
| S-031 | [Restrict resource types and cap logging](S-031-restrict-resource-types-and-cap-logging.md) | Cloud and costs | S-029 | High | S | `done` |
| S-042 | [Publish the showcase site at sepiabox.com](S-042-publish-the-showcase-site-at-sepiabox-com.md) | Site | — | Low | S | `done` |

### Sprint 2 — See the photos in the browser (done)

**Demonstrable increment:** After `upload`, sign in locally with Google or Microsoft and browse the drive's photos and videos by date, with CLIP embeddings and (opt-in) faces computed in the same pass; e-mails not on the list are refused.

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-007 | [Upload originals, derivatives and manifests to storage](S-007-upload-originals-derivatives-and-manifests-to-storage.md) | Local ingest | S-003, S-004 | High | M | `done` |
| S-008 | [Import manifests into the catalog](S-008-import-manifests-into-the-catalog.md) | Cloud catalog | S-003, S-007 | High | M | `done` |
| S-009 | [Browse the library on a timeline](S-009-browse-the-library-on-a-timeline.md) | Web app | S-008 | High | M | `done` |
| S-015 | [Compute CLIP embeddings during processing](S-015-compute-clip-embeddings-during-processing.md) | Search | S-005, S-008 | High | M | `done` |
| S-018 | [Detect faces during processing](S-018-detect-faces-during-processing.md) | People | S-005, S-015 | High | M | `done` |
| S-010 | [Sign in with Google or Microsoft and allow only the access list](S-010-sign-in-with-google-or-microsoft-and.md) | Web app | S-009 | High | M | `done` |
| S-011 | [Watch videos on the detail page](S-011-watch-videos-on-the-detail-page.md) | Web app | S-006, S-010 | Medium | S | `done` |

### Sprint 3 — English UI and video locations

**Demonstrable increment:** The app speaks English or Brazilian Portuguese with English routes; `process` reads the location of phone videos, before the first full run over the drive.

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-044 | [Localize the web app: English first, Brazilian Portuguese](S-044-localize-the-web-app-english-first-brazilian.md) | Web app | S-011 | High | M | `done` |
| S-055 | [Rename the web routes to English](S-055-rename-the-web-routes-to-english.md) | Web app | S-044 | High | S | `done` |
| S-053 | [Read video locations from QuickTime metadata](S-053-read-video-locations-from-quicktime-metadata.md) | Local ingest | S-004 | High | S | `done` |

### Sprint 4 — The app in Azure

**Demonstrable increment:** The app runs in Azure at the Container Apps address after the migrations, with managed identities, Key Vault secrets and sign-in that survives restarts; the uploader can write to storage.

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-032 | [Publish the app to Azure with azd](S-032-publish-the-app-to-azure-with-azd.md) | Cloud and costs | S-010, S-031 | High | M | `done` |
| S-058 | [Give the Azure services their identities and secrets](S-058-give-the-azure-services-their-identities.md) | Cloud and costs | S-032 | High | M | `done` |

### Sprint 5 — Deploys and automatic import

**Demonstrable increment:** A manifest uploaded from the PC is imported without intervention, with a backfill job and a dead-letter check; pushes to `main` deploy through GitHub Actions; `app.sepiabox.com` is ready to point at the app.

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-033 | [Import manifests automatically when they arrive](S-033-import-manifests-automatically-when-they-arrive.md) | Cloud and costs | S-032, S-008, S-058 | High | M | `done` |
| S-034 | [Deploy from GitHub Actions with OIDC](S-034-deploy-from-github-actions-with-oidc.md) | Cloud and costs | S-032 | Medium | S | `done` |
| S-035 | [Serve the app at app.sepiabox.com](S-035-serve-the-app-at-app-sepiabox-com.md) | Cloud and costs | S-032, S-058 | Low | S | `done` |

### Sprint 6 — Place names and a fixture library

**Demonstrable increment:** Photos show their place, locally and in Azure; tests can generate a folder of photos and videos whose counts are known.

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-012 | [Name places with GeoNames](S-012-name-places-with-geonames.md) | Cloud catalog | S-008 | High | M | `done` |
| S-059 | [Load GeoNames in Azure](S-059-load-geonames-in-azure.md) | Cloud catalog | S-012, S-058 | High | S | `done` |
| S-061 | [Generate a fixture library with known contents](S-061-generate-a-fixture-library-with-known.md) | Questions | S-004, S-053 | High | S | `done` |

### Sprint 7 — Filters and description search

**Demonstrable increment:** Filter by "Lisbon, December 2019", kind, source and "on this day"; search "beach at sunset".

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-013 | [Filter the library with search DSL v1](S-013-filter-the-library-with-search-dsl-v1.md) | Search | S-012 | High | M | `done` |
| S-016 | [Search photos by description](S-016-search-photos-by-description.md) | Search | S-013, S-015 | High | M | `done` |

### Sprint 8 — Questions and a count reference

**Demonstrable increment:** Description search works in Azure; ask "videos from Lisbon in December 2019" (or in Portuguese) and get the right photos and videos, checked against a golden set; `sepiabox-ingest report` counts unique assets in a work folder, the reference for correct counts.

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-056 | [Ship the CLIP text model with the web in Azure](S-056-ship-the-clip-text-model-with-the.md) | Search | S-016, S-032 | High | S | `done` |
| S-025 | [Ask questions in English or Brazilian Portuguese with Claude Haiku](S-025-ask-questions-in-english-or-brazilian-portuguese.md) | Questions | S-016, S-044 | Medium | M | `done` |
| S-062 | [Count unique assets in a work folder](S-062-count-unique-assets-in-a-work-folder.md) | Local ingest | S-004, S-043 | High | S | `done` |

### Sprint 9 — Counting answers and MVP acceptance (end of the MVP)

**Demonstrable increment:** Ask "How many videos did we take in 2019?" and get the correct number; an end-to-end test takes a generated folder from `process` to answers and every count matches the unique-asset report; the owner compares a few counts on the real data in Azure with `sepiabox-ingest report`. **End of the MVP.**

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-026 | [Answer counting questions](S-026-answer-counting-questions.md) | Questions | S-025 | Medium | M | `done` |
| S-054 | [Accept the MVP from drive to answers](S-054-accept-the-mvp-from-drive-to-answers.md) | Questions | S-026, S-061, S-062, S-012 | High | M | `done` |

### After the MVP

The sprints below come after the MVP ([D-053](../decisions.md)), in this order, and do not start while an MVP story is `blocked` ([D-054](../decisions.md)).

### Sprint 10 — People I

**Demonstrable increment:** Faces are grouped into people, which the family names, merges and corrects.

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-019 | [Group faces into people](S-019-group-faces-into-people.md) | People | S-018, S-010 | High | M | `done` |
| S-020 | [Name, merge and correct people](S-020-name-merge-and-correct-people.md) | People | S-019 | High | M | `done` |

### Sprint 11 — People II

**Demonstrable increment:** Filter and ask "photos of me with Ana and Pedro"; turning recognition off deletes face data.

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-021 | [Filter by people](S-021-filter-by-people.md) | People | S-020, S-013, S-026 | High | M | `done` |
| S-022 | [Turn off face recognition and delete face data](S-022-turn-off-face-recognition-and-delete-face.md) | People | S-019 | Medium | S | `done` |

### Sprint 12 — Personal places, similar photos and video locations refresh

**Demonstrable increment:** Save "Beach house" and "home", filter and ask by them; open "more like this"; work folders processed before S-053 get their video locations.

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-014 | [Save personal places and filter by them](S-014-save-personal-places-and-filter-by-them.md) | Search | S-013, S-025 | Medium | M | `done` |
| S-017 | [Show more photos similar to this one](S-017-show-more-photos-similar-to-this-one.md) | Search | S-015, S-013 | Medium | S | `done` |
| S-060 | [Refresh video locations in processed work folders](S-060-refresh-video-locations-in-processed-work-folders.md) | Local ingest | S-053 | Low | S | `done` |

### Sprint 13 — Index-only mode, occasions and storage view

**Demonstrable increment:** Index-only mode (`sepiabox-ingest process --index-only`) keeps the originals where they are: `upload` sends 2048 px previews, 1080p videos, embeddings, faces and manifests but no original; the detail page shows the larger preview and where the original lives, with a link when the source has one ([D-088](../decisions.md)); the library page shows the space each source takes. In parallel, photos are grouped into typed occasions and "How many weddings did we go to in 2026?" is answered.

**Tracks** ([D-090](../decisions.md)): the two tracks run in parallel, one implementer each, and no story of one depends on a story of the other. **Track A:** index-only mode in `sepiabox-ingest`, then the detail page and the storage view. **Track B:** occasions in the catalog, search and questions. Stories are listed in the order of each track.

| Story | Title | Epic | Depends on | Priority | Size | Status | Track |
|---|---|---|---|---|---|---|---|
| S-063 | [Process and upload a source in index-only mode](S-063-process-and-upload-a-source-in-index-only.md) | Local ingest | S-005, S-006, S-007, S-008, S-015, S-018 | Medium | M | `done` | A |
| S-064 | [Show where an index-only original lives](S-064-show-where-an-index-only-original-lives.md) | Web app | S-063, S-011, S-044 | Medium | S | `done` | A |
| S-028 | [See the library's storage and sources](S-028-see-the-library-s-storage-and-sources.md) | Organization | S-026 | Low | S | `done` | A |
| S-023 | [Group photos into occasions](S-023-group-photos-into-occasions.md) | Occasions | S-012 | Medium | M | `done` | B |
| S-024 | [Classify occasions by type](S-024-classify-occasions-by-type.md) | Occasions | S-023, S-016, S-026, S-014 | Medium | M | `done` | B |

### Sprint 14 — Portability, Google Drive and tidying the library

**Demonstrable increment:** Existing storage code goes through the project's own interfaces and originals upload through them, with behaviour unchanged ([D-051](../decisions.md)); a Google Drive account can be connected; blurry photos and bursts are listed and photos similar to a selected region are found.

**Tracks** ([D-090](../decisions.md)): the two tracks run in parallel, one implementer each, and no story of one depends on a story of the other. **Track A:** portability refactor of the storage code (ingest, worker, web storage layer), then region search on the new interface (it reads previews through that same storage code). **Track B:** Google Drive connection, blurry photos and bursts. Stories are listed in the order of each track.

| Story | Title | Epic | Depends on | Priority | Size | Status | Track |
|---|---|---|---|---|---|---|---|
| S-050 | [Put blob storage behind the project's own interface](S-050-put-blob-storage-behind-the-project-s.md) | Foundation | S-007, S-008, S-009, S-033 | High | M | `doing` | A |
| S-051 | [Upload originals through the storage interface](S-051-upload-originals-through-the-storage-interface.md) | Foundation | S-050, S-063 | Medium | M | `done` | A |
| S-041 | [Find photos similar to a selected region](S-041-find-photos-similar-to-a-selected-region.md) | Search | S-017, S-023 | Low | S | `doing` | A |
| S-037 | [Connect a Google Drive account](S-037-connect-a-google-drive-account.md) | Cloud sources | S-010 | Medium | M | `done` | B |
| S-027 | [Find blurry photos and bursts](S-027-find-blurry-photos-and-bursts.md) | Organization | S-005, S-013 | Low | M | `done` | B |

### Sprint 15 — Cloud processing, source deletion, export and Takeout

**Demonstrable increment:** Files that arrive in the cloud are processed by the worker; everything that came only from one named source can be deleted safely; the full export as a zip works. In parallel, `sepiabox-ingest process --export <kind>` reads an export's dates, places, captions and albums ([D-053](../decisions.md)), and a Google Takeout export shows up in the timeline with its metadata, in either mode.

**Tracks** ([D-090](../decisions.md)): the two tracks run in parallel, one implementer each, and no story of one depends on a story of the other. **Track A:** worker, catalog and export (cloud processing, deletion, full export). **Track B:** export metadata in `sepiabox-ingest` and the Takeout importer. Stories are listed in the order of each track.

| Story | Title | Epic | Depends on | Priority | Size | Status | Track |
|---|---|---|---|---|---|---|---|
| S-036 | [Process files that arrive in the cloud without a manifest](S-036-process-files-that-arrive-in-the-cloud.md) | Cloud sources | S-033, S-006, S-015, S-050 | Medium | M | `todo` | A |
| S-057 | [Delete a source's assets from the library](S-057-delete-a-source-s-assets-from-the.md) | Cloud catalog | S-008, S-050, S-058 | Medium | M | `todo` | A |
| S-040 | [Export all data](S-040-export-all-data.md) | Privacy | S-020, S-010, S-051, S-063 | Medium | S | `todo` | A |
| S-045 | [Read metadata from photo exports during process](S-045-read-metadata-from-photo-exports-during-process.md) | Import exports | S-004, S-043 | Medium | M | `done` | B |
| S-046 | [Import a Google Takeout export of Google Photos](S-046-import-a-google-takeout-export-of.md) | Import exports | S-004, S-043, S-045, S-063 | Medium | M | `doing` | B |

### Sprint 16 — Drive and OneDrive sync, Apple Photos and social exports

**Demonstrable increment:** Photos from Google Drive and OneDrive show up in the timeline, and the app shows captions and albums and filters by album; an Apple Photos / iCloud export, and Instagram and Facebook downloads, show up in the timeline with their captions and albums, in either mode.

**Tracks** ([D-090](../decisions.md)): the two tracks run in parallel, one implementer each, and no story of one depends on a story of the other. **Track A:** cloud source sync (worker and web), then the caption and album view (after the export and source deletion of Sprint 15, which it extends). **Track B:** export importers in `sepiabox-ingest`. Stories are listed in the order of each track.

| Story | Title | Epic | Depends on | Priority | Size | Status | Track |
|---|---|---|---|---|---|---|---|
| S-038 | [Sync Google Drive photos in reference mode](S-038-sync-google-drive-photos-in-reference-mode.md) | Cloud sources | S-037, S-036, S-064 | Medium | M | `todo` | A |
| S-039 | [Connect and sync OneDrive](S-039-connect-and-sync-onedrive.md) | Cloud sources | S-036, S-010, S-064 | Medium | M | `todo` | A |
| S-052 | [Show captions and albums and filter by album](S-052-show-captions-and-albums-and-filter-by-album.md) | Import exports | S-045, S-013, S-044 | Medium | S | `todo` | A |
| S-047 | [Import an Apple Photos or iCloud Photos export](S-047-import-an-apple-photos-or-icloud.md) | Import exports | S-004, S-043, S-045, S-063 | Medium | M | `doing` | B |
| S-048 | [Import an Instagram data download](S-048-import-an-instagram-data-download.md) | Import exports | S-004, S-043, S-045, S-063 | Medium | M | `doing` | B |
| S-049 | [Import a Facebook data download](S-049-import-a-facebook-data-download.md) | Import exports | S-004, S-043, S-045, S-048 | Medium | S | `todo` | B |

## Epics

| Epic | Stories |
|---|---|
| Foundation | S-001, S-003, S-050, S-051 |
| Local ingest | S-002, S-004, S-005, S-006, S-007, S-043, S-053, S-060, S-062, S-063 |
| Cloud catalog | S-008, S-012, S-057, S-059 |
| Web app | S-009, S-010, S-011, S-044, S-055, S-064 |
| Search | S-013, S-014, S-015, S-016, S-017, S-041, S-056 |
| People | S-018, S-019, S-020, S-021, S-022 |
| Occasions | S-023, S-024 |
| Questions | S-025, S-026, S-054, S-061 |
| Organization | S-027, S-028 |
| Cloud and costs | S-029, S-030, S-031, S-032, S-033, S-034, S-035, S-058 |
| Cloud sources | S-036, S-037, S-038, S-039 |
| Privacy | S-040 |
| Site | S-042 |
| Import exports | S-045, S-046, S-047, S-048, S-049, S-052 |

## Validation

- **Dependencies:** all point to stories listed above them, in the same or an earlier sprint (S-044 and S-050–S-062 were added with D-050, D-051, D-053 and the reviews of D-054, and S-063–S-064 with D-088; the sprints were re-cut by D-054, D-088 put index-only mode right after Sprint 12 at the owner's choice, and D-090 regrouped Sprints 13–16 into two parallel tracks); there are no cycles and no forward dependencies: a story's dependencies sit in an earlier sprint or earlier in the same track, and no story depends on the other track of its sprint (checked by script when this index is generated).
- **Size:** 22 stories S, 42 M, none L.
- **Sprints:** 16 sprints. Sprints 3–12 hold 2 or 3 stories each (M + M, or up to 3 when some are S, D-054); Sprints 13–16 hold two tracks of 2–3 stories each, 5 or 6 stories per sprint (D-090); Sprints 1 (11 stories) and 2 (7) gather the work done before D-054.
- **Decisions covered:** D-001 to D-024 in [`decisions.md`](../decisions.md) are applied: each is cited in the technical notes of at least one story, except D-021, which this backlog itself applies.
- Story status is kept up to date by each story's PR (see [How to use](#how-to-use)).

### Discussed in the session but outside this backlog

| Topic | Reason |
|---|---|
| Dropbox | Not used by the family today; the source model (S-037 to S-039) allows adding it later. Google Photos, Apple Photos / iCloud, Instagram and Facebook are now covered by Sprints 15–16 through their exports (D-053), with index-only mode (Sprint 13, D-088) for keeping their originals where they are. |
| A local UI for pre-processing (a future epic) | A nicer desktop interface for `scan`, `process --export` and `upload` (progress, problems, choosing folders). For now the `sepiabox-ingest` console is enough. |
| Mobile apps and Sign in with Apple | Phase 3; Apple sign-in only makes sense with the iOS app (D-006). |
| Dedicated object and clothing detection ("red dress") | In v1 covered by CLIP visual search (S-016, S-041); a dedicated detector is phase 3. |
| OCR and video transcription | Phase 3; processing cost without a target question in v1. |
| Relationship graph and vision-model captions ("Paulo's son") | Phase 3; needs relationships between people and a generative vision model. |
| Per-user encryption (crypto-shredding) | Only needed for the multi-family product; two users in a shared library (D-018). |
| Business plan, pricing and billing | Lives in the private `sepiabox-planning` repository; there is no sellable product in v1. |
| `sepiabox-site` in English first | The public site is in Portuguese today; D-050 makes it English first with `pt-BR` later, and the footer's pt-PT "logótipo" becomes "logo". A small post-MVP follow-up in `sepiabox-site`: [sepiabox/sepiabox-site#2](https://github.com/sepiabox/sepiabox-site/issues/2). |
| Outlined wordmark and site waiting list | Needs a design tool / a backend; the S-042 site uses the symbol and the name as text, with no form. |
| Human steps (subscription, OIDC, OAuth clients, DNS, Anthropic API key) | Cannot be automated; listed in [`operations.md`](../operations.md) and referenced by the stories that depend on them. |

## Summary

- **Epics:** 14
- **Sprints:** 16 (Sprints 13–16 with two parallel tracks, D-090)
- **Stories:** 64
- **MVP** ([D-053](../decisions.md)): Sprints 1–9 (36 stories), with the external drive as the only source; Sprints 10–16 (28 stories) come after it. The MVP is done (all 36 stories); its longest chain is 12 stories (S-001 → … → S-010 → S-011 → S-044 → S-025 → S-026 → S-054).
- **Critical path (13 stories):** S-001 → S-002 → S-004 → S-007 → S-008 → S-009 → S-010 → S-032 → S-058 → S-033 → S-050 → S-036 → S-038, and others as long: the same chain ending in S-036 → S-039, or in S-050 → S-051 → S-040. Index-only mode (S-063, S-064) depends only on done stories, so it adds nothing to it (D-088). With the parallel tracks of D-090 the chain runs through Sprints 14 (S-050), 15 (S-036) and 16 (S-038 or S-039), so Sprints 14–16 are the shortest possible plan.
