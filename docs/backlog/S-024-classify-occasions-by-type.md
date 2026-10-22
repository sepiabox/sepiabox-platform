## S-024 — Classify occasions by type
**Epic:** Occasions
**Depends on:** S-023, S-016, S-026, S-014
**Priority:** Medium
**Size:** M

### Context
Knowing that an occasion is a wedding uses the CLIP embeddings already stored (zero-shot), with no new model (D-019).

### Scope
- `eventTypes.json`: English keys with their CLIP prompt: `wedding` ("a photo of a wedding"), `birthday` ("a photo of a birthday party"), `beach` ("a photo at the beach"), `christmas` ("a photo of a Christmas celebration"), `graduation` ("a photo of a graduation ceremony"), `snow` ("a photo in the snow"), `party` ("a photo of a party"). Labels shown to the user come from the resources (English and pt-BR: casamento, aniversário, praia, Natal, formatura, neve, festa, viagem; D-050). No religion or health categories.
- Per photo: softmax (temperature 100) across the texts; event type = the label with the most photos at probability ≥ 0.35, if that is ≥ 30% of the photos; otherwise no type.
- Extra `trip` rule: an occasion more than 100 km from the place marked as home (S-014) lasting ≥ 1 day; without a home place, no occasion is a trip.
- DSL `eventTypes: ["wedding"]`; filter on the occasions page.
- Counting (S-026): `aggregate.count: "events"` and `groupBy: "eventType"` ("How many weddings did we go to this year?"), with the answer sentence in both cultures.
- Questions (S-025): the translator's context gains the occasion types and its prompt examples map them ("casamentos" → `wedding`).

- Classifies every occasion that has no type, including those whose type a rebuild cleared because their assets changed (`events rebuild` keeps a type only when an occasion's assets are exactly the same, S-023, D-092). Occasions that already have a type are left as they are.

### Out of scope
- Counting photos (S-026, already there).

### Acceptance criteria
- [x] Given synthetic embeddings aligned with "wedding" in 60% of the photos, when the occasion is classified, then the type is `wedding`. *(`OccasionClassifierTests`, `OccasionClassifyTests`.)*
- [x] Given no label above the threshold in 30% of the photos, when the occasion is classified, then the type stays empty. *(Two of ten stay empty; three of ten is exactly 30% and wins.)*
- [x] Given a 2-day occasion 300 km from the home place, when it is classified, then it has the type `trip`. *(`OccasionClassifyTests`: with no home place it has none; a type from the photos wins over a trip, D-094.)*
- [x] Given `eventTypes=[wedding]`, when occasions are searched, then only occasions of that type are returned. *(`OccasionsCatalogTests`: the DSL filter on assets, and the occasions page's `?type=`.)*
- [x] Given 4 occasions of type `wedding` in 2026, when I count `events` with `eventTypes=[wedding]` and `years=[2026]`, then `total` is 4. *(`OccasionsCatalogTests`, and the golden question `events-weddings-2026`.)*

- [x] Given the golden set's questions about occasions, `birthday-in-sintra` (now an occasion type) and the four `events-*` ones (English and Brazilian Portuguese), when they run with the real API before `done` (D-054), then each means its expected query on the fixture catalog, as in S-025 (one recorded retry per question). *(Recorded on `story/S-024-classify-occasions-by-type`: `Model claude-haiku-5-5, 2026-10-22: 118/118 passed, 0 after a retry ().` The MVP acceptance run, which shares the output format, was re-recorded too: `16/16 answered right, 0 after a retry ()`.)*

### Technical notes
- Text embeddings computed once (S-016) and kept in memory; tests use a fake encoder.
- Decisions: D-019, D-050, D-094.

### Expected tests
- Unit: voting, thresholds, trip. Integration: filter.

### Recording run
In a session that has `SEPIABOX_ANTHROPIC_API_KEY` and Docker, on this story's branch:
1. `git fetch origin && git checkout story/S-024-classify-occasions-by-type && git pull`
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
3. `SEPIABOX_RECORD_GOLDEN=1 dotnet test tests/Sepiabox.Web.Tests --filter "Category=LiveApi" --logger "console;verbosity=detailed"`. It must report `Passed: 1` (`Skipped: 1` means the key is not set); its output ends with `Model claude-haiku-5-5, <date>: <passed>/118 passed, <n> after a retry (<ids>).` and lists every miss. Only when every question passed does it rewrite `questions.recorded.json` (all 118 responses, `placeholders` removed) and print `Recorded <path>.`; otherwise it prints `Not recording: N failed, ...` and leaves the file as it was (D-076).
4. `dotnet test tests/Sepiabox.Web.Tests --filter "FullyQualifiedName~GoldenQuestions&Category!=LiveApi"` (the CI replay, now on the recorded responses) must pass.
5. `git add tests/Sepiabox.Web.Tests/TestData/questions.recorded.json`, tick the last acceptance criterion with the run's line from step 3, set S-024 to `done` in `docs/backlog/README.md` and in the Status below, commit, push, and put the run's line in the PR description.

If the run fails, keep the story `blocked` (the recorded file is unchanged) and report the misses: they are prompt or golden-set fixes for a review iteration. The prompt now says what an occasion type is and when to use `eventTypes` (QuestionPrompt, "Occasions"): a miss on `birthday-in-sintra` is likely the prompt's wording about a birthday named with a person and a place.

### Status
- `done` (2026-10-22): implemented and reviewed (D-094); the live golden run passed 118/118 and was recorded (D-054).
