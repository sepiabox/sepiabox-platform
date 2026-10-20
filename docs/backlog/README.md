# Sepiabox backlog

Backlog meant to be executed by autonomous agents. Each story is a small vertical slice (up to ~400 changed lines), depends only on earlier stories and carries all the context it needs. Sources: the product design conversation and [`docs/`](..). Decisions taken by default are in [`docs/decisions.md`](../decisions.md).

## How to use

1. Pick the first `todo` story in the order below whose dependencies are all `done`.
2. Set its status to `doing` in this table, in the same PR as the implementation.
3. Implement until every acceptance criterion and expected test passes and CI is green.
4. Set the status to `done` in the PR. If a human step is missing ([`docs/operations.md`](../operations.md)), set `blocked` and say which.

Statuses: `todo` · `doing` · `blocked` · `done`.

**Order overrides** ([D-041](../decisions.md)): after S-009 come S-015, S-043, then S-018, ahead of S-010–S-014, so the drive is processed once with embeddings, faces and the dates of undated files.

## Execution order

### Sprint 1 — Foundation and first read of the drive

**Demonstrable increment:** `sepiabox-ingest process` produces manifests, thumbnails and 720p versions for a real folder; `dotnet run --project src/Sepiabox.AppHost` starts PostgreSQL and Azurite.

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-001 | [Set up the repository foundation](S-001-set-up-the-repository-foundation.md) | Foundation | — | High | M | `done` |
| S-002 | [Scan a drive and report what it contains](S-002-scan-a-drive-and-report-what-it.md) | Local ingest | S-001 | High | M | `done` |
| S-003 | [Start the local environment with .NET Aspire](S-003-start-the-local-environment-with-net-aspire.md) | Foundation | S-001 | High | M | `done` |
| S-004 | [Describe each photo and video in a manifest](S-004-describe-each-photo-and-video-in-a.md) | Local ingest | S-002 | High | M | `done` |
| S-005 | [Generate photo thumbnails](S-005-generate-photo-thumbnails.md) | Local ingest | S-004 | High | S | `done` |
| S-006 | [Generate video posters and 720p versions](S-006-generate-video-posters-and-720p-versions.md) | Local ingest | S-004 | High | M | `done` |
| S-043 | [Date files from folder and file names](S-043-date-files-from-folder-and-file-names.md) | Local ingest | S-004 | High | M | `done` |

### Sprint 2 — See the photos in the browser

**Demonstrable increment:** After `upload`, sign in locally with Google or Microsoft and browse the drive's photos and videos by date; e-mails not on the list are refused.

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-007 | [Upload originals, derivatives and manifests to storage](S-007-upload-originals-derivatives-and-manifests-to-storage.md) | Local ingest | S-003, S-004 | High | M | `done` |
| S-008 | [Import manifests into the catalog](S-008-import-manifests-into-the-catalog.md) | Cloud catalog | S-003, S-007 | High | M | `done` |
| S-009 | [Browse the library on a timeline](S-009-browse-the-library-on-a-timeline.md) | Web app | S-008 | High | M | `done` |
| S-010 | [Sign in with Google or Microsoft and allow only the access list](S-010-sign-in-with-google-or-microsoft-and.md) | Web app | S-009 | High | M | `done` |
| S-011 | [Watch videos on the detail page](S-011-watch-videos-on-the-detail-page.md) | Web app | S-006, S-010 | Medium | S | `done` |

### Sprint 3 — Find by date, place and similarity

**Demonstrable increment:** Filter "Lisbon, December 2019", search "beach at sunset" and open "more like this".

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-012 | [Name places with GeoNames](S-012-name-places-with-geonames.md) | Cloud catalog | S-008 | High | M | `todo` |
| S-013 | [Filter the library with search DSL v1](S-013-filter-the-library-with-search-dsl-v1.md) | Search | S-012 | High | M | `todo` |
| S-014 | [Save personal places and filter by them](S-014-save-personal-places-and-filter-by-them.md) | Search | S-013 | Medium | S | `todo` |
| S-015 | [Compute CLIP embeddings during processing](S-015-compute-clip-embeddings-during-processing.md) | Search | S-005, S-008 | High | M | `done` |
| S-016 | [Search photos by description](S-016-search-photos-by-description.md) | Search | S-013, S-015 | High | M | `todo` |
| S-017 | [Show more photos similar to this one](S-017-show-more-photos-similar-to-this-one.md) | Search | S-015, S-013 | Medium | S | `todo` |

### Sprint 4 — People (opt-in)

**Demonstrable increment:** Name face clusters and filter "photos of me with Ana and Pedro"; turning recognition off deletes face data.

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-018 | [Detect faces during processing](S-018-detect-faces-during-processing.md) | People | S-005, S-015 | High | M | `done` |
| S-019 | [Group faces into people](S-019-group-faces-into-people.md) | People | S-018, S-010 | High | M | `todo` |
| S-020 | [Name, merge and correct people](S-020-name-merge-and-correct-people.md) | People | S-019 | High | M | `todo` |
| S-021 | [Filter by people](S-021-filter-by-people.md) | People | S-020, S-013 | High | S | `todo` |
| S-022 | [Turn off face recognition and delete face data](S-022-turn-off-face-recognition-and-delete-face.md) | People | S-019 | Medium | S | `todo` |

### Sprint 5 — Occasions and questions

**Demonstrable increment:** Ask "Quantos casamentos fomos em 2026?" (how many weddings did we go to in 2026?) and get the number with the occasions; see blurry photos, bursts and space per source.

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-023 | [Group photos into occasions](S-023-group-photos-into-occasions.md) | Occasions | S-012 | Medium | M | `todo` |
| S-024 | [Classify occasions by type](S-024-classify-occasions-by-type.md) | Occasions | S-023, S-016 | Medium | M | `todo` |
| S-025 | [Ask questions in Portuguese with Claude Haiku](S-025-ask-questions-in-portuguese-with-claude-haiku.md) | Questions | S-016, S-021, S-024, S-014 | Medium | M | `todo` |
| S-026 | [Answer counting questions](S-026-answer-counting-questions.md) | Questions | S-025 | Medium | M | `todo` |
| S-027 | [Find blurry photos and bursts](S-027-find-blurry-photos-and-bursts.md) | Organization | S-005, S-013 | Low | M | `todo` |
| S-028 | [See the library's storage and sources](S-028-see-the-library-s-storage-and-sources.md) | Organization | S-026 | Low | S | `todo` |

### Sprint 6 — In the cloud with costs under control

**Demonstrable increment:** The app runs at `app.sepiabox.com`, deployed by GitHub Actions, with budget, alerts and automatic shutdown at €45 active.

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-029 | [Protect the budget with alerts](S-029-protect-the-budget-with-alerts.md) | Cloud and costs | — | High | S | `done` |
| S-030 | [Shut everything down automatically at €45](S-030-shut-everything-down-automatically-at-45.md) | Cloud and costs | S-029 | High | M | `done` |
| S-031 | [Restrict resource types and cap logging](S-031-restrict-resource-types-and-cap-logging.md) | Cloud and costs | S-029 | High | S | `done` |
| S-032 | [Publish the app to Azure with azd](S-032-publish-the-app-to-azure-with-azd.md) | Cloud and costs | S-010, S-031 | High | M | `todo` |
| S-033 | [Import manifests automatically when they arrive](S-033-import-manifests-automatically-when-they-arrive.md) | Cloud and costs | S-032, S-008 | High | M | `todo` |
| S-034 | [Deploy from GitHub Actions with OIDC](S-034-deploy-from-github-actions-with-oidc.md) | Cloud and costs | S-032 | Medium | S | `todo` |
| S-035 | [Serve the app at app.sepiabox.com](S-035-serve-the-app-at-app-sepiabox-com.md) | Cloud and costs | S-032 | Low | S | `todo` |

### Sprint 7 — Cloud sources, privacy and site

**Demonstrable increment:** Photos from Google Drive and OneDrive show up in the timeline; full export as a zip; `sepiabox.com` published.

| Story | Title | Epic | Depends on | Priority | Size | Status |
|---|---|---|---|---|---|---|
| S-036 | [Process files that arrive in the cloud without a manifest](S-036-process-files-that-arrive-in-the-cloud.md) | Cloud sources | S-033, S-006, S-015 | Medium | M | `todo` |
| S-037 | [Connect a Google Drive account](S-037-connect-a-google-drive-account.md) | Cloud sources | S-010 | Medium | M | `todo` |
| S-038 | [Sync Google Drive photos in reference mode](S-038-sync-google-drive-photos-in-reference-mode.md) | Cloud sources | S-037, S-036 | Medium | M | `todo` |
| S-039 | [Connect and sync OneDrive](S-039-connect-and-sync-onedrive.md) | Cloud sources | S-036, S-010 | Medium | M | `todo` |
| S-040 | [Export all data](S-040-export-all-data.md) | Privacy | S-020, S-010 | Medium | S | `todo` |
| S-041 | [Find photos similar to a selected region](S-041-find-photos-similar-to-a-selected-region.md) | Search | S-017, S-023 | Low | S | `todo` |
| S-042 | [Publish the showcase site at sepiabox.com](S-042-publish-the-showcase-site-at-sepiabox-com.md) | Site | — | Low | S | `done` |

## Epics

| Epic | Stories |
|---|---|
| Foundation | S-001, S-003 |
| Local ingest | S-002, S-004, S-005, S-006, S-007, S-043 |
| Cloud catalog | S-008, S-012 |
| Web app | S-009, S-010, S-011 |
| Search | S-013, S-014, S-015, S-016, S-017, S-041 |
| People | S-018, S-019, S-020, S-021, S-022 |
| Occasions | S-023, S-024 |
| Questions | S-025, S-026 |
| Organization | S-027, S-028 |
| Cloud and costs | S-029, S-030, S-031, S-032, S-033, S-034, S-035 |
| Cloud sources | S-036, S-037, S-038, S-039 |
| Privacy | S-040 |
| Site | S-042 |

## Validation

- **Dependencies:** all point to stories in the same or an earlier sprint (S-043 was added later and listed in Sprint 1); there are no cycles and no forward dependencies (checked by script when this index is generated).
- **Size:** 14 stories S, 29 M, none L.
- **Sprints:** 7 sprints, between 5 and 7 stories each.
- **Decisions covered:** D-001 to D-024 in [`decisions.md`](../decisions.md) are applied: each is cited in the technical notes of at least one story, except D-021, which this backlog itself applies.
- Story status is kept up to date by each story's PR (see [How to use](#how-to-use)).

### Discussed in the session but outside this backlog

| Topic | Reason |
|---|---|
| Google Photos (Picker API / Takeout with sidecar JSON) | The Library API no longer gives access to the whole library; a Takeout export works as a local folder and can be read by `process` once extracted. Deferred until there is real use. |
| Dropbox and iCloud | Not used by the family today; the source model (S-037 to S-039) allows adding them later. |
| Mobile apps and Sign in with Apple | Phase 3; Apple sign-in only makes sense with the iOS app (D-006). |
| Dedicated object and clothing detection ("red dress") | In v1 covered by CLIP visual search (S-016, S-041); a dedicated detector is phase 3. |
| OCR and video transcription | Phase 3; processing cost without a target question in v1. |
| Relationship graph and vision-model captions ("Paulo's son") | Phase 3; needs relationships between people and a generative vision model. |
| Per-user encryption (crypto-shredding) | Only needed for the multi-family product; two users in a shared library (D-018). |
| Business plan, pricing and billing | Lives in the private `sepiabox-planning` repository; there is no sellable product in v1. |
| Outlined wordmark and site waiting list | Needs a design tool / a backend; the S-042 site uses the symbol and the name as text, with no form. |
| Human steps (subscription, OIDC, OAuth clients, DNS, Foundry) | Cannot be automated; listed in [`operations.md`](../operations.md) and referenced by the stories that depend on them. |

## Summary

- **Epics:** 13
- **Sprints:** 7
- **Stories:** 43
- **Critical path (13 stories):** S-001 → S-002 → S-004 → S-007 → S-008 → S-015 → S-018 → S-019 → S-020 → S-021 → S-025 → S-026 → S-028
