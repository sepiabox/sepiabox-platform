# Review of the backlog alignment (docs/align-goals)

Process: [docs/process.md](../process.md). Docs-only change, reviewed by Atlas (Spock reviews code only).

## Iteration 1

Diff: `main...docs/align-goals` at `d97af60`, 40 files. Atlas confirmed the index matches every story file, dependencies are backward-only, every MVP story depends only on MVP stories, counts are right, the budget wording is consistent, no pt-PT in new strings, post-MVP sources come through exports, local processing and the console UI are kept.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P1 | Atlas | Phone videos keep their location in QuickTime metadata, not EXIF; place questions about videos return nothing | ✅ Yes (iteration 2) | New MVP story to read the QuickTime location, with a refresh path |
| P1 | Atlas | The CLIP text model and ONNX Runtime have no way into the web container; contradicts D-038 | ✅ Yes (iteration 2) | Decide project and model delivery in S-016; AC in Production |
| P1 | Atlas | GeoNames is never loaded into the Azure database | ✅ Yes (iteration 2) | Cloud job and ops step in S-012 |
| P1 | Atlas | Manifests uploaded before S-033 or dead-lettered are never imported; worker identity unspecified | ✅ Yes (iteration 2) | Backfill job, roles, dead-letter check, manual AC |
| P1 | Atlas | Whoever runs `upload` has no blob data rights; the first MVP step fails with 403 | ✅ Yes (iteration 2) | Human prerequisite and upload section in operations.md |
| P1 | Atlas | "Correct answers" never verified | ✅ Yes (iteration 2) | Golden question set, required real-API pass, MVP acceptance story |
| P2 | Atlas | A blocked cloud story would let agents move past the MVP | ✅ Yes (iteration 2) | Process rule: manual checks as owner acceptance; no post-MVP sprint while an MVP story is blocked |
| P2 | Atlas | S-032 references a domain S-035 creates; default host lacks redirect URIs | ✅ Yes (iteration 2) | Default FQDN and its redirect URIs |
| P2 | Atlas | Budget runbook stops resources by names nothing guarantees | ✅ Yes (iteration 2) | Name ACs or azd outputs |
| P2 | Atlas | Counts with description filters or undated items mislead | ✅ Yes (iteration 2) | Exclude or label; show undated count |
| P3 | Atlas | Storage role, Anthropic key AC, D-054 wording, portability notes, migrate before deploy, pt-PT text in operations.md, site language, S-044 size | ✅ Yes (iteration 2) | Fix |
| P3 | Atlas | Cognitive Services still allowed in sepiabox-infra | ✅ Yes | sepiabox/sepiabox-infra#5 merged |
| P4 | Atlas | Critical path list, architecture examples, S-024 dependency | ✅ Yes (iteration 2) | Fix |

Decision: P1 open → iteration 2.

## Iteration 2

Diff: `d97af60...0b3b60b` (4 commits). The MVP grew to Sprints 1–8 (S-053, S-055, S-056, S-054). Atlas confirmed all 21 iteration-1 findings resolved, the index consistent (56 stories), dependencies backward and MVP dependencies inside the MVP.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P1 | Atlas | S-054's Azure run leaves test assets in the family's only library; nothing deletes assets | ✅ Yes (iteration 3) | Delete-by-source command story; unique source and far-past dates; mandatory cleanup |
| P1 | Atlas | S-054 cannot sign in to the web from a script; locally there is no automatic import | ✅ Yes (iteration 3) | Questions through a CLI on the same services; owner checks the web once; `import --once` locally |
| P2 | Atlas | S-054 is M; S-032 and Sprints 4, 5 exceed ~4 h | ✅ Yes (iteration 3) | Resize, split S-032 and S-012's Azure job |
| P2 | Atlas | A missing model would fail the probe and take the whole web down | ✅ Yes (iteration 3) | Separate health tag; 503 for description search; AC |
| P2 | Atlas | The 250 MB model in every image fills ACR Basic | ✅ Yes (iteration 3) | Stable layer; `acr purge --keep 5`; correct D-055 |
| P2 | Atlas | The agent never has the Anthropic key, so S-025/S-026 always block | ✅ Yes (iteration 3) | Owner adds the key as an environment secret (human step) |
| P2 | Atlas | Golden questions compared by form, not meaning | ✅ Yes (iteration 3) | Compare result sets on a fixture catalog; one retry |
| P3 | Atlas | S-053 refresh not needed before the first full run; blocked dependencies; S-016 without the model; site follow-up | ✅ Yes (iteration 3) | Fix; site issue opened separately |

Decision: P1 open → iteration 3.

## Iteration 3

Diff: `0b3b60b...2829632` (4 commits). The MVP grew to Sprints 1–9 (S-057, S-058, S-059 added; S-060 after). Atlas confirmed every iteration-2 finding resolved (S-054's size only partly), 1901 fixture dates compatible with D-046, `az containerapp job start --args` sound, D-056 acceptable.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P1 | Atlas | Deleting by source trusts the catalog alone; a manifest blob may already list the family's drive, and the shared original would be lost for good | ✅ Yes (iteration 4) | Design decision: no cloud deletes in the MVP; S-057 moves after the MVP with blob-checked, ETag-conditional deletion and D-036 amended |
| P1 | Atlas | The Azure acceptance run cannot pass: read-only identity, input files on the PC, no job named, questions counting the family's library | ✅ Yes (iteration 4) | Acceptance becomes an automated end-to-end test in CI plus an owner check on real data in Azure against `scan` |
| P2 | Atlas | Same-name sources and duplicates in delete-by-source | ✅ Yes (iteration 4) | In S-057 (post-MVP) |
| P2 | Atlas | S-054 too heavy for Sprint 9 | ✅ Yes (iteration 4) | Simpler acceptance; split the fixture generator if needed |
| P2 | Atlas | Broken human-steps table in operations.md | ✅ Yes (iteration 4) | Fix |
| P2 | Atlas | Worker identity gets the Anthropic key | ✅ Yes (iteration 4) | Only the web gets it |
| P2 | Atlas | `az acr purge` is not a CLI command | ✅ Yes (iteration 4) | `az acr run --cmd "acr purge …"`; AcrDelete role |

Decision: P1 open → iteration 4, simplifying the MVP acceptance.

## Iteration 4

Diff: `2829632...0521a52` (3 commits). No test data reaches the family's Azure library: S-054 is an in-process end-to-end test in CI (fixture from new S-061) plus an owner check on real data; S-057 moved after the MVP with the safety rules (D-057). Atlas confirmed every iteration-3 finding resolved or moot, the index consistent (61 stories), the MVP sprints within the rule, the live-run key setup sound and goals 1–8 respected.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P1 | Atlas | "Correct" measured against `scan`, which counts files (duplicates), mixes kinds per year and has no cities | ✅ Yes (iteration 5) | A report of unique assets per year × kind from the manifests; use it in S-054 and the owner check |
| P2 | Atlas | The fixture has no truly undated files | ✅ Yes (iteration 5) | Add them to S-061 |
| P2 | Atlas | Post-MVP delete can remove an original that a concurrent upload just re-used | ✅ Yes (iteration 5) | Re-read the manifest after deleting; "re-upload needed"; ops note |

Decision: P1 open → iteration 5.

## Iteration 5

Diff: `0521a52...3455397` (2 commits). New story S-062 (`sepiabox-ingest report`, unique assets per year × kind and undated) in Sprint 8 is the reference for S-054 and the owner check; S-061 has truly undated files; S-057 re-reads the manifest after deleting. Atlas confirmed the three iteration-4 findings resolved, the index consistent (62 stories, 62 files), dependencies backward, the MVP (Sprints 1–9, 36 stories) closed under its dependencies, Sprint 8 within the ~4 h rule, no stale `scan --report` reference, and goals 1–8 respected.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3 | Atlas | The owner check compares the catalog with `report` on one work folder, but the catalog holds every folder ever uploaded | ✅ Yes (with this record) | `report` takes several work folders in upload order and counts each asset once |
| P3 | Atlas | Dates follow the last uploaded manifest (D-036), `report` its own folder | ✅ Yes (with this record) | The last folder given wins, as in the cloud; acceptance criterion in S-062 |

Decision: no P0–P2 open → pull request.
