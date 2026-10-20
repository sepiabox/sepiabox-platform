## S-054 — Accept the MVP from drive to answers
**Epic:** Questions
**Depends on:** S-026, S-061, S-012
**Priority:** High
**Size:** M

### Context
The MVP's goal (D-053): take the data from the external drive to the cloud, ask questions about the imported photos and videos, and get correct answers. Each story tests its own part; this story checks the whole path once, end to end, with numbers known in advance, and closes the MVP. It never touches the family's cloud library: the automated test runs against Azurite and a test PostgreSQL, and the check on real data in Azure is an owner acceptance step (D-056).

### Scope
- An end-to-end test (Integration, Linux CI): the fixture library of S-061 goes through `scan --report` → `process` → `upload` to Azurite → `sepiabox-worker import --once` → place resolution with a small GeoNames sample (S-012) → the golden counting questions of S-026, answered in-process through the same `IQuestionTranslator` (`Sepiabox.Search`) and search service the web uses, in a test host. Each answer must equal the number from `scan`'s report and the generator.
- The same test in `Category=LiveApi` with the real Anthropic API, required before `done` under D-054's key rule; recorded responses in CI.
- An "MVP acceptance" section in `docs/operations.md`: on the real data in Azure, after uploading the drive, ask in the web a few counting questions (photos per year, videos per year, photos in one city) and compare with the `scan` report of the same drive.

### Out of scope
- Uploading any fixture or test data to Azure.
- A command-line or API route for questions (D-056).

### Acceptance criteria
- [ ] Given the fixture library and recorded translator responses, when the end-to-end test runs in CI, then every golden counting question's answer equals the expected number, and a mismatch fails with the question and both numbers.
- [ ] Given the same test with the real API (`Category=LiveApi`), when it runs before `done`, then every answer matches, compared by meaning as in S-025 (one recorded retry per question); the result goes in the PR.
- [ ] Given the fixture's duplicate, undated files and videos with locations, then the counts treat them as the product does (one asset per content, undated reported apart, videos counted in their city).
- [ ] Given `docs/operations.md`, then it lists the owner's acceptance on the real data (questions to ask and how to compare them with `scan`).

### Technical notes
- The test host wires the catalog, the search service and the translator as the web's `Program.cs` does, without HTTP or sign-in.
- Decisions: D-005, D-046, D-053, D-054, D-056.

### Expected tests
- The end-to-end test, recorded (CI) and live (before `done`).
