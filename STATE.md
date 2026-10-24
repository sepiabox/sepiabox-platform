# Team state, paused 2026-10-22 21:20Z (resume 2026-10-23 afternoon)

The owner paused all development until the weekly session limits reset. Main is at 52bbc10: S-038, the CI timeout fix (#127) and the merge-direction fix (#126, closes #125) are merged.

## Agents (resume each with SendMessage to its ID; if the IDs are gone, re-brief from the role below)
| Agent | ID | Role | Was doing | Next |
|---|---|---|---|---|
| Tesla | a910e283523dcd649 | implementer | idle, waiting on Atlas's S-039 findings | fix Atlas's S-039 findings, if any |
| Edison | a46313b91d843b10f | implementer | idle | after S-066 merges: merge main into S-067 (#123), then S-068 |
| Atlas | a9b17e225bfafbd65 | architecture reviewer | reviewing S-039 (#128) when stopped | redo the S-039 review (b61adcc) |
| Spock | a2e2e42007b018221 | diff/mutation reviewer (model: sonnet) | #110 migration re-check when stopped | #110 re-check (7cbc9bf), #116 full, #120 full, S-039 re-check (3dc54f1..b61adcc) |
| Sherlock | a67846519463588ed | second diff reviewer, same setup as Spock (sonnet), prefix sherlock- | starting #115 light pass when stopped | light passes #115, #123, #124 in that order, one report each; test PersonCovers and AssetDetail.Faces |
| DaVinci | a8a1ca96080aca607 | UI/UX reviewer | idle | none queued |

## Open PRs
| PR | Item | Head | Left |
|---|---|---|---|
| #110 | #107 ignore faces | 7cbc9bf | Spock's quick OK on the restored Down, then CI, then close |
| #128 | S-039 OneDrive (draft) | b61adcc | Atlas review, Spock re-check, CI (b61adcc tags OneDriveConnectionTests and OneDriveOriginalTests as Integration after the Windows failure) |
| #116 | S-065 | ec62222 | Spock full review (Atlas approved) |
| #120 | #111 strip faces | 0f70024 | Spock full review (Atlas approved) |
| #115 | S-066 | f3635ef | Sherlock light pass (Atlas, DaVinci approved), then merge first |
| #123 | S-067, stacked on S-066 | 0ea5039 | Sherlock light pass; merge main after S-066 lands |
| #124 | S-068, stacked on S-066+S-067 | bd37c0d | Sherlock light pass; merge after S-067 |

## Infrastructure
- Docker is stopped and the restart check-in is deleted. On resume: `rm -f /var/run/docker.pid`, start `dockerd` in the background with a 2 h limit, and schedule a restart check-in for about 1 h 55 min later. Images `sepiabox-postgres-local:test` and `sepiabox-postgres-tests:latest` (a retag of it).
- PR subscriptions are all off; re-subscribe to whatever is open.
- Boards are up to date as of 21:25Z: backlog dataset version 76, blob 41ad2124cb2e2eb1b8d1950ad98049c2 (delete it at the next update); sprint board card I-125 is done; cards for S-039, S-066, I-107, I-125, S-065, I-111, S-067 and S-068 updated.
- Next D-number: D-126.

## First thing on resume: a pipeline bug found by the owner's upload
- The upload finished on 2026-10-23 (87,888 assets, 865.9 GB with derivatives). 17 manifests failed on DNS errors (retry the same upload) and **8 were rejected as invalid**: their EXIF GPS is garbage (latitude/longitude around -436774), and `MetadataReader.cs:81` takes `TryGetGeoLocation` without a range check (ExportFacts, AppleXmp and Iso6709 do check it). Fix: drop a location outside -90..90 / -180..180 (and NaN), with a test; open an issue, then the owner re-processes those files and uploads. The "/facesProcessed: Expected false" in the same message is the schema's other branch and goes away with the location.

## Second thing on resume: occasion types ignore the calendar
- The owner ran import-all, `events rebuild` (3,496 occasions, 87,410 assets) and `events classify` (2,049 typed, 0 trips since no home place, 1,447 without a type) on 2026-10-23. Some types are clearly wrong, e.g. **christmas in April**: `OccasionClassifier` (src/Sepiabox.Catalog/Events/OccasionClassifier.cs) votes by CLIP alone (softmax T=100, min 0.35) with no date rule. Proposal: a calendar gate for date-bound types (christmas only 1 Dec to 6 Jan; fall back to the next type or none), plus a way to clear types that break the gate, since `events classify` never revisits a typed occasion. Asked the owner for other wrong types (type and month only). New D-number; Atlas + Spock/Sherlock review; the owner re-runs classify locally afterwards (firewall rule + Key Vault connection string, as on 2026-10-23).

## Owner feedback on the Places page (for S-069 and S-070, and DaVinci)
- The owner finds **Places** (`/places`) ugly on main (2026-10-23 screenshot, not kept: it showed real coordinates). Concretely: the whole "New place" form sits on one wrapping line (name, an inline hint, coordinates, radius, the "our home" checkbox, then an unstyled button below); the hint about Claude reads as a fragment beside the field; the empty state is a bare sentence. S-066 (#115) fixes the base controls and S-069 moves Places onto the shared components, S-070 adds its empty state: brief Tesla/Edison and DaVinci with these points when S-069 starts (stacked form with labels above fields, the hint as field help, the home checkbox explained, a real empty state). Because a trip needs a home place, the form is also how the owner unlocks trips: make "this is our home" easy to find.

## Waiting on the owner
- Nothing blocking. The owner's ingest was at 314 of 706 GB (44%) at about 21:30Z on 2026-10-22. When it ends: sepiabox-import-all, events rebuild, events classify, report.

## Parked on 2026-10-24 (owner: UI and UX first, nothing else)
- **#130 EXIF GPS (d9af658):** Atlas approved incl. the repair (MetadataVersion 3 drops off-globe locations once). Open: Atlas/Sherlock P4 that a corrupt work-folder manifest now throws in `Decide` (make it "no repair" + test); Sherlock's mutation run on the repair path not done.
- **Occasion calendar fix (christmas in April):** not started; plan was a calendar gate (christmas 1 Dec - 6 Jan) plus clearing types that break it, D-128, branch fix/occasion-calendar.
- **#120 strip faces (0f70024):** Spock half done (16/38 mutants), no bug; P3 survivors: `All` vs `Any` over several remembered boxes, the 0.5 IoU edge (add ~0.4), the full-pass decision (no checkpoint / no last-full / `>` vs `>=`), `ErasedAt > since` boundary. Not run: deadline D1-D4, counters, StripFaces/ManifestStore mutants.
- **#128 S-039 OneDrive (b3bdc0a):** Atlas approved; Tesla removed the duplicate StagedBlockStream and merged main; Spock's re-check of the test fixes not started.
- **Redesign plan (DaVinci audit, D-126 desktop first):** Tesla S-069 primitives, then S-071 Places, S-072 Settings+Export, S-073 Sign-in; Edison closes S-067/S-068 then S-074 Person/Merge/People, S-075 Occasions+Organize, S-076 Storage+Sources, theme switch.
