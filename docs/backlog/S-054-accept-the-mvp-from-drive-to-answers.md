## S-054 — Accept the MVP from drive to answers
**Epic:** Questions
**Depends on:** S-026, S-061, S-062, S-012
**Priority:** High
**Size:** M

### Context
The MVP's goal (D-053): take the data from the external drive to the cloud, ask questions about the imported photos and videos, and get correct answers. Each story tests its own part; this story checks the whole path once, end to end, with numbers known in advance, and closes the MVP. It never touches the family's cloud library: the automated test runs against Azurite and a test PostgreSQL, and the check on real data in Azure is an owner acceptance step (D-056).

### Scope
- An end-to-end test (Integration, Linux CI): the fixture library of S-061 goes through `process` → `sepiabox-ingest report --json` (S-062) → `upload` to Azurite → `sepiabox-worker import --once` → place resolution with a small GeoNames sample (S-012) → the golden counting questions of S-026, answered in-process through the same `IQuestionTranslator` (`Sepiabox.Search`) and search service the web uses, in a test host. Expected numbers are **unique-asset** counts: per year × kind and undated from `report`, per city from the generator (S-061), which must agree with `report` on the totals.
- The same test in `Category=LiveApi` with the real Anthropic API, required before `done` under D-054's key rule; recorded responses in CI.
- An "MVP acceptance" section in `docs/operations.md`: on the real data in Azure, after uploading the drive, ask in the web a few counting questions (photos per year, videos per year, items with no date) and compare with `sepiabox-ingest report` on the work folder that was uploaded.

### Out of scope
- Uploading any fixture or test data to Azure.
- A command-line or API route for questions (D-056).

### Acceptance criteria
- [x] Given the fixture library and recorded translator responses, when the end-to-end test runs in CI, then every golden counting question's answer equals the expected number, and a mismatch fails with the question and both numbers. *(`MvpAcceptanceTests` in `tests/Sepiabox.Acceptance.Tests`, D-076: 8 questions × 2 languages pass on the recorded responses, which are the expected queries until the recording run; a wrong response fails with lines such as `photos-by-year/pt "quantas fotos por ano?": group 2005: expected 3 (report), answered 1`.)*
- [ ] Given the same test with the real API (`Category=LiveApi`), when it runs before `done`, then every answer matches, compared by meaning as in S-025 (one recorded retry per question); the result goes in the PR.
- [x] Given the fixture's exact duplicate, then it counts once in both `report` and the answer (unique assets, not files); given its folder-dated files, they count in their year; given its truly undated files, the time-filtered answers say "N items have no date" with N equal to `report`'s undated count; given its videos with locations, they count in their city. *(One test each: the duplicate, the 1901 folder-dated photos, the undated count of a time-filtered answer for photos and for all items, and the Lisbon and Porto videos.)*
- [x] Given `docs/operations.md`, then it lists the owner's acceptance on the real data (questions to ask and how to compare them with `sepiabox-ingest report`). *("MVP acceptance": the commands, the import checks, the questions in both languages and a table to fill in.)*

### Technical notes
- The test host wires the catalog, the search service and the translator as the web's `Program.cs` does, without HTTP or sign-in.
- Decisions: D-005, D-014, D-046, D-053, D-054, D-056, D-076.

### Recording run
In a session that has `SEPIABOX_ANTHROPIC_API_KEY`, Docker, and `ffmpeg` and `ffprobe` on the PATH, on this story's branch:
1. `git fetch origin && git checkout story/S-054-mvp-acceptance && git pull`
2. `docker info` must answer (start `dockerd` if it does not), and `ffmpeg -version` and `ffprobe -version` must too. Build the test PostgreSQL image without network `RUN` steps, as for S-025 and S-026 (the agents' container cannot reach the Debian mirrors; the pgvector stage must be bookworm):
   ```bash
   mkdir -p /tmp/pgtest && cat > /tmp/pgtest/Dockerfile <<'DOCKERFILE'
   FROM pgvector/pgvector:0.8.1-pg17-bookworm AS vec
   FROM postgis/postgis:17-3.5
   COPY --from=vec /usr/lib/postgresql/17/lib/vector.so /usr/lib/postgresql/17/lib/
   COPY --from=vec /usr/share/postgresql/17/extension/vector* /usr/share/postgresql/17/extension/
   DOCKERFILE
   docker build -t sepiabox-postgres-local:test /tmp/pgtest
   export SEPIABOX_TEST_POSTGRES_IMAGE=sepiabox-postgres-local:test
   ```
3. `SEPIABOX_RECORD_GOLDEN=1 dotnet test tests/Sepiabox.Acceptance.Tests --filter "Category=LiveApi" --logger "console;verbosity=detailed"`. It must report `Passed: 1` (`Skipped: 1` means the key is not set); its output ends with `Model claude-haiku-5-5, <date>: <passed>/16 answered right, <n> after a retry (<ids>).` and lists every miss with both numbers. When every question passed (retries allowed) it rewrites every response of `tests/Sepiabox.Acceptance.Tests/TestData/acceptance.recorded.json`, removes its `placeholders` and prints `Recorded <path>.`; otherwise it prints `Not recording: N failed, ...` and leaves the file as it was.
4. `dotnet test tests/Sepiabox.Acceptance.Tests --filter "FullyQualifiedName~MvpAcceptance&Category!=LiveApi"` (the CI replay, now on the recorded responses) must pass: `Passed: 7`.
5. `git add tests/Sepiabox.Acceptance.Tests/TestData/acceptance.recorded.json`, tick the second acceptance criterion with the run's line, set S-054 to `done` in `docs/backlog/README.md` and in the Status below, commit, push, and put the run's line from step 3 in the PR description.

If the run fails, keep the story `blocked` (the recorded file is unchanged, so there is nothing to commit) and report the misses: they are prompt or question fixes for a review iteration.

### Status
- `blocked` (2026-10-21): everything is implemented and tested (D-076) but the required live run with recording (D-054), above.

### Expected tests
- The end-to-end test, recorded (CI) and live (before `done`).
