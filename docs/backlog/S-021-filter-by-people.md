## S-021 — Filter by people
**Epic:** People
**Depends on:** S-020, S-013, S-026
**Priority:** High
**Size:** M

### Context
"Photos of me with Ana and Pedro together" ("Fotos minhas com a Ana e o Pedro juntos") is the most common question.

### Scope
- DSL: `people: { "all": ["Ana", "Pedro"], "includeMe": true }` (all the people in the same photo; `includeMe` resolves the current user's "This is me").
- People selector in the filter bar; the photo detail lists the people with a link to the filter.
- Questions (S-025): the translator's context gains the people's names and "me", and its prompt examples cover people ("photos of me with Ana and Pedro").
- Counting (S-026): `groupBy: "person"` ("Who did I take the most photos with in 2025?").

### Out of scope

### Acceptance criteria
- [x] Given photos with Ana, Pedro and both, when I filter `all=[Ana,Pedro]`, then only those with both come back. *(`PeopleSearchTests`: names ignoring case; excluded and not-a-face faces never count.)*
- [x] Given `includeMe` for a user with "This is me", then that filter is applied. *(`PeopleSearchTests`; a user without "This is me" gets the message, `me: true` in a 400.)*
- [x] Given an unknown name, then the API returns 400 with the missing name. *(`PeopleSearchTests`: `POST /api/search` answers 400 with `missing: ["Zé"]` and `field: filters.people`; `/api/questions` the same with the query.)*
- [x] Given photos with several people in 2025, when I group by person with `top=3`, then the 3 with the most photos are returned, in order. *(`PeopleSearchTests`: the people the filters name are left out of the groups, D-080.)*
- [x] Given existing people, when the question prompt is built, then it includes their names. *(`PeopleFilterTests` and `GoldenQuestionsTests`: names only, never an address.)*

- [ ] Given the golden set's five people questions (English and Brazilian Portuguese), when they run with the real API before `done` (D-054), then each means its expected query on the fixture catalog, as in S-025 (one recorded retry per question).

### Technical notes
- Extends `search.v1.json` and the executor.
- Decisions: D-018, D-073, D-077, D-079, D-080.

### Recording run
In a session that has `SEPIABOX_ANTHROPIC_API_KEY` and Docker, on this story's branch:
1. `git fetch origin && git checkout story/S-021-filter-by-people && git pull`
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
3. `SEPIABOX_RECORD_GOLDEN=1 dotnet test tests/Sepiabox.Web.Tests --filter "Category=LiveApi" --logger "console;verbosity=detailed"`. It must report `Passed: 1` (`Skipped: 1` means the key is not set); its output ends with `Model claude-haiku-5-5, <date>: <passed>/98 passed, <n> after a retry (<ids>).` and lists every miss. Only when every question passed does it rewrite `questions.recorded.json` (all 98 responses, `placeholders` removed) and print `Recorded <path>.`; otherwise it prints `Not recording: N failed, ...` and leaves the file as it was (D-076).
4. `dotnet test tests/Sepiabox.Web.Tests --filter "FullyQualifiedName~GoldenQuestions&Category!=LiveApi"` (the CI replay, now on the recorded responses) must pass.
5. `git add tests/Sepiabox.Web.Tests/TestData/questions.recorded.json`, tick the last acceptance criterion with the run's line from step 3, set S-021 to `done` in `docs/backlog/README.md` and in the Status below, commit, push, and put the run's line in the PR description.

If the run fails, keep the story `blocked` (the recorded file is unchanged) and report the misses: they are prompt or golden-set fixes for a review iteration.

### Status
- `blocked` (2026-10-21): everything is implemented and tested (D-080) but the required live golden run with recording (D-054), above.

### Expected tests
- Integration (PostgreSQL): "all" semantics, `includeMe`, errors.
