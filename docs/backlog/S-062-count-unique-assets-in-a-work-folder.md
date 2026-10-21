## S-062 — Count unique assets in a work folder
**Epic:** Local ingest
**Depends on:** S-004, S-043
**Priority:** High
**Size:** S

### Context
"Correct answers" (D-053) need a reference in the catalog's unit. `scan --report` counts files: an exact duplicate counts twice, photos and videos are mixed per year, and it knows no places. The catalog counts unique assets (one per content, D-014). The work folder's manifests are exactly the assets `upload` sends, so they are the right reference for the MVP acceptance (S-054) and for the owner's check on real data.

### Scope
- `sepiabox-ingest report <workdir>...`: reads every manifest in the given work folders and prints unique assets per year × kind (photo, video), the undated count, the total, and how many assets have more than one source; `--json <file>` writes the same numbers.
- Years come from `capturedAt` as stored (wall-clock, D-037), whatever `capturedAtSource` says; undated means no `capturedAt`.
- Several work folders (several drives, or a re-run into a fresh folder) are given in upload order: an asset in more than one counts once, and its date and kind come from the last folder that has it, as the cloud's merged manifest takes the last upload's description (D-036).
- Manifests that fail validation are listed as problems and not counted.

### Out of scope
- Counts per city: they need GeoNames (S-012), which lives in the catalog; the owner's check on real data leaves out the city question, and the end-to-end test takes city counts from the fixture generator (S-061).
- Counts per source name (`scan` per drive already gives an idea, and the catalog's per-source counts come with S-028).

### Acceptance criteria
- [x] Given a work folder with 3 photos in 2019 (one of them found at two paths), 2 videos in 2019, 1 photo in 2020 and 2 undated files, when `report` runs, then it prints 2019: 3 photos, 2 videos; 2020: 1 photo; undated: 2; total 8; 1 with more than one source. *(`AssetReportTests`; integration `ReportFixtureTests` on the S-061 fixture library after `process`.)*
- [x] Given two work folders that share an asset dated differently, when `report` runs on both, then the asset counts once, in the year of the folder given last. *(Both orders; for metadata dates. A path date loses to a metadata date whichever folder comes last, as in the cloud's merge (D-074).)*
- [x] Given `--json`, then the file holds the same numbers in a documented shape that S-054 reads. *(`docs/schemas/ingest-report.v1.json`; the test validates against it.)*
- [x] Given an invalid manifest in the folder, then it is listed as a problem, not counted, and the exit code is 1.

### Technical notes
- Reuse `ManifestValidator` and the manifest reader of `upload`.
- Decisions: D-014, D-036, D-037, D-046.

### Expected tests
- Unit: counting over generated manifests (duplicates, undated, both kinds). Integration: `process` then `report` on a small generated folder.
