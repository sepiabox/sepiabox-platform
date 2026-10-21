## S-026 — Answer counting questions
**Epic:** Questions
**Depends on:** S-025
**Priority:** Medium
**Size:** M

### Context
"How many videos did we take in 2019?" ("Quantos vídeos fizemos em 2019?") and "In which year did we take the most photos in Lisbon?" ("Em que ano tiramos mais fotos em Lisboa?") ask for numbers, not grids. In the MVP, counts use the MVP filters (dates, places, kinds, sources, description); counting occasions (S-024) and grouping by person (S-021) extend this later.

### Scope
- DSL: `aggregate: { "count": "assets", "groupBy": null | "year" | "month" | "kind" | "city" | "source", "top": 10 }`, designed so later stories add `count: "events"` and other `groupBy` values.
- The executor returns `{ total, groups: [{ key, count }] }`.
- Counts are exact only over exact filters: a count with a `visual` (description) filter is not shown as a number in the MVP; the card says the question needs a description search and shows the results instead. (A later count with `visual` must use `SimilarityMode.Exact` and a calibrated threshold, D-070.) When the query has a time filter, the card also says "N items have no date" (undated items cannot match a date) with a link to them.
- UI: answer card (big number + a sentence in the current culture, e.g. "312 videos in 2019" / "312 vídeos em 2019", with plural forms from the resources) or a ranking table, with a link to the items.
- Counting examples in the translator prompt, in English and Portuguese.

### Out of scope
- Byte sums (S-028).
- Counting occasions and grouping by occasion type (S-024) or by person (S-021).

### Acceptance criteria
- [x] Given 4 videos and 6 photos in 2019, when I count `assets` with `kinds=[video]` and `years=[2019]`, then `total` is 4. *(`SearchCountTests`, PostgreSQL.)*
- [x] Given photos in Lisbon across several years, when I group by year with `cities=[Lisbon]` and `top=3`, then the 3 years with the most photos are returned, in order. *(`SearchCountTests`.)*
- [x] Given the question "how many videos did we take in 2019?" and a fake translator returning that count, when it is asked, then the answer card shows 4. *(`CountPageTests`, in English and Portuguese.)*
- [x] Given a counting answer, when it is displayed, then the card shows the number and the sentence, in English and in pt-BR. *(`CountAnswerTests`, bUnit.)*
- [x] Given a count with a `visual` filter, when it is executed, then no number is returned and the UI shows the results with the explanation. *(`SearchCountTests`, `CountPageTests`; the APIs answer `notCounted` with the items.)*
- [x] Given 3 undated assets and a count with `years=[2019]`, when it is displayed, then the card shows "3 items have no date" next to the number. *(`SearchCountTests` counts them; `CountAnswerTests` shows them, with a link.)*
- [ ] Given the golden set of S-025, when at least 10 counting questions (English and Brazilian Portuguese: by year, kind, place, source, "on this day") with their expected DSL are added, then they pass in CI (recorded) and in the required live run before `done`, compared by meaning as in S-025: the expected and returned DSL are counted against the seeded fixture catalog and the numbers must be equal (one recorded retry per question). *(Not ticked: 12 counting questions are added (and `count-videos-2018` now expects a count), the CI replay passes on their expected queries (placeholders in `questions.recorded.json`); the recording run below is still to be made.)*

### Technical notes
- Extends `search.v1.json`, the executor and the prompt.
- Portability (D-051): counts and groupings use plain EF Core `GroupBy` in the search service; nothing provider-specific.
- Decisions: D-020, D-051, D-053, D-054.

### Recording run
In a session that has `SEPIABOX_ANTHROPIC_API_KEY` and Docker, on this story's branch:
1. `git fetch origin && git checkout story/S-026-counting-questions && git pull`
2. `docker info` must answer (start `dockerd` if it does not). Build the test PostgreSQL image without network `RUN` steps, as for S-025 (the agents' container cannot reach the Debian mirrors; the pgvector stage must be bookworm):
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
3. `SEPIABOX_RECORD_GOLDEN=1 dotnet test tests/Sepiabox.Web.Tests --filter "Category=LiveApi" --logger "console;verbosity=detailed"`. It must report `Passed: 1` (`Skipped: 1` means the key is not set); its output ends with `Model claude-haiku-5-5, <date>: <passed>/88 passed, <n> after a retry (<ids>).` and lists every miss. It rewrites every response of `questions.recorded.json` and removes its `placeholders`.
4. `dotnet test tests/Sepiabox.Web.Tests --filter "FullyQualifiedName~GoldenQuestions"` (the CI replay, now on the recorded responses) must pass.
5. `git add tests/Sepiabox.Web.Tests/TestData/questions.recorded.json`, tick the last acceptance criterion, set S-026 to `done` in `docs/backlog/README.md`, commit, push, and put the run's line from step 3 in the PR description.

If the run fails, keep the story `blocked`, do not commit the recorded file, and report the misses: they are prompt or golden-set fixes for a review iteration.

### Status
- `blocked` (2026-10-21): everything is implemented and tested (D-075) but the required live golden run with recording (D-054), above.

### Expected tests
- Integration (PostgreSQL): counts and groupings. bUnit: answer card.
