## S-054 — Accept the MVP from drive to answers
**Epic:** Questions
**Depends on:** S-026, S-033, S-053, S-056, S-057, S-059
**Priority:** High
**Size:** M

### Context
The MVP's goal (D-053): take the data from the external drive to the cloud, ask questions about the imported photos and videos, and get correct answers. Each story tests its own part; this story checks the whole path once, with numbers known in advance, and closes the MVP. A script cannot sign in to the web (Google or Microsoft sign-in), so the questions run through a worker command that uses the same translator and search services (D-056); the owner checks the web by hand once.

### Scope
- `sepiabox-worker ask "<question>"` prints the interpreted DSL and the answer (count or result ids) using `IQuestionTranslator` (`Sepiabox.Search`) and the catalog's search service, exactly as the web does; `sepiabox-worker acceptance --golden <file> --expected <file>` asks every golden counting question and exits 1 listing each mismatch (question, expected, answered).
- `scripts/mvp-acceptance` (PowerShell 7, Windows and Linux): generates a fixture folder whose source name is `mvp-acceptance` and whose files are dated in 1901 through `1901-MM-DD` folder names (metadata dates before 1990 are ignored, D-046, so the files carry no EXIF or QuickTime date), with photos with EXIF GPS in Lisbon and Porto, phone-style videos with QuickTime location (S-053), files in another `1901-...` folder without GPS, and an exact duplicate; runs `scan --report`, `process`, `upload`, waits for the import, runs `acceptance` with the expected numbers computed from the generated folder and `scan`'s report, then deletes the fixture with `assets delete --source mvp-acceptance --yes` (S-057) and checks the catalog counts are back to where they started.
- Two targets: **local** (the AppHost with Azurite and PostgreSQL; the script runs `sepiabox-worker import --once` after `upload`; recorded translator responses) and **Azure** (`upload --destination azure`, the automatic import of S-033; `acceptance` and `assets delete` run as executions of the worker job image with `az containerapp job start ... --args`, using the real Anthropic API; the script checks each execution's exit status).
- A short "MVP acceptance" section in `docs/operations.md`: the command for each target, what a pass looks like, and the one manual web check.

### Out of scope
- The owner's real drive (that is the real use, after the MVP closes).
- Any anonymous or API-key route into the web (D-056).

### Acceptance criteria
- [ ] Given the local target, when the script runs in CI (Integration, Linux), then every golden counting question matches the expected number, and a mismatch fails the run with the question and both numbers.
- [ ] Given either target, when the script finishes (pass or fail), then the `mvp-acceptance` assets are deleted and the catalog's asset count and the blob counts per container equal those before the run (mandatory; the script fails if not).
- [ ] Given the fixture, then no generated date falls inside the family's range (all in 1901) and every source is named `mvp-acceptance`, so a leftover is easy to find and cannot be mistaken for a family photo.
- [ ] Given the Azure target (manual, owner acceptance), when the owner runs the script after a deploy, then every answer matches; and before the deletion step (the script pauses with `-PauseBeforeCleanup`), the owner sees one fixture Lisbon photo in the web with "Lisbon" on its detail page and asks one counting question in the web with the same answer.

### Technical notes
- Reuse the golden set of S-025/S-026; the expected numbers come from the generated folder, never typed by hand.
- Early deletion of the fixture's few Cold-tier originals costs a fraction of a cent (S-057).
- Decisions: D-005, D-046, D-053, D-054, D-056.

### Expected tests
- The script in CI against the local target; unit tests for `ask` and `acceptance` with a fake translator.
