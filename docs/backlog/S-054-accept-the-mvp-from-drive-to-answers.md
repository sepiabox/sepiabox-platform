## S-054 — Accept the MVP from drive to answers
**Epic:** Questions
**Depends on:** S-026, S-033, S-012, S-053, S-056
**Priority:** High
**Size:** S

### Context
The MVP's goal (D-053): take the data from the external drive to the cloud, ask questions about the imported photos and videos, and get correct answers. Each story tests its own part; this story checks the whole path once, with numbers known in advance, and closes the MVP.

### Scope
- `scripts/mvp-acceptance` (PowerShell 7, runs on Windows and Linux): generates a fixture folder with known contents (photos with EXIF dates and GPS in two cities, phone-style videos with QuickTime location, undated files in a `YYYY-MM-DD` folder, an exact duplicate), runs `scan --report`, `process`, `upload`, waits until the automatic import has the assets, then asks the golden counting questions (S-026) through the web's API and compares each answer with the numbers computed from `scan`'s report and the generated folder.
- Two targets: local (the AppHost with Azurite and PostgreSQL, and recorded translator responses) and Azure (the deployed app, the real Anthropic API, `upload --destination azure`).
- A short "MVP acceptance" section in `docs/operations.md` with the command and what a pass looks like.

### Out of scope
- The owner's real drive (that is the real use, after the MVP closes).

### Acceptance criteria
- [ ] Given the local target, when the script runs in CI (Integration, Linux), then every golden counting question matches the expected number, and it fails with the question and both numbers when one does not.
- [ ] Given the Azure target (manual, owner acceptance), when the owner runs the script after a deploy, then every answer matches and the fixture's Lisbon photos show "Lisbon" on their detail page.
- [ ] Given the script, when it finishes, then it removes the fixture's assets from the target or names them so they can be removed (no test data left in the family library).

### Technical notes
- Reuse the golden set of S-025/S-026; the expected numbers come from the generated folder, never typed by hand.
- Decisions: D-005, D-053, D-054.

### Expected tests
- The script itself in CI against the local target.
