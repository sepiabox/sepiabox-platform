## S-014 — Save personal places and filter by them
**Epic:** Search
**Depends on:** S-013, S-025
**Priority:** Medium
**Size:** M

### Context
"The beach house over the years" ("A casa da praia ao longo dos anos") and "photos more than 100 km from home" ("fotos a mais de 100 km de casa") depend on places only the family knows.

### Scope
- Table `saved_places` (id, name unique case-insensitively, location, radius_m, is_home). At most one place is the home: marking another moves the mark. Names are what the family types; "home" is a flag, not a name, so it works in any language (D-050).
- Page `/places`: create from a photo's location ("Save this place"; "Salvar este lugar" in pt-BR) or from lat/lon, edit the radius, delete.
- DSL: `savedPlaces: ["Beach house"]`, `distanceFrom: { "place": "Beach house", "minKm": 100 }` or `distanceFrom: { "home": true, "minKm": 100 }`.
- "Over the years" view for a place (grouped by year).
- Questions (S-025): the translator's context gains the saved place names and the home place, with prompt examples ("photos at the beach house", "more than 100 km from home").

### Out of scope
- Automatic detection of "home".

### Acceptance criteria
- [x] Given a place with a 200 m radius, then the filter returns only assets inside the radius. *(`SavedPlacesSearchTests`: 150 m and 190 m in, 250 m and no location out; the name matched ignoring case.)*
- [x] Given `distanceFrom` the home place with `minKm=100`, then only assets more than 100 km away come back. *(`SavedPlacesSearchTests`: 105 km and 500 km in, 95 km, 0 km and no location out; `maxKm`, both bounds and a named place too.)*
- [x] Given a place marked as home, when another place is marked as home, then only the second keeps the mark. *(`SavedPlacesSearchTests`, creating and changing a place.)*
- [x] Given a deleted place, then the assets remain intact. *(`SavedPlacesSearchTests`: the assets and their locations unchanged; a search naming it then reports it unknown.)*
- [x] Given two places with the same name in different letter case, then the second is rejected. *(`SavedPlacesSearchTests`, on create and on rename, a double space too; backed by a unique index on `lower(name)`.)*
- [ ] The live golden run (D-054) passes with the six new place questions (12 asks, 110 in all) and records them; until then they are placeholders in `questions.recorded.json` (see "Recording run").

### Technical notes
- `ST_DWithin` on `geography`, in the PostGIS-specific class next to S-012's (D-051). Extends `search.v1.json`.
- Decisions: D-050, D-051, D-073, D-075, D-076, D-080, D-083.

### Recording run
In a session that has `SEPIABOX_ANTHROPIC_API_KEY` and Docker, on this story's branch:
1. `git fetch origin && git checkout story/S-014-personal-places && git pull`
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
3. `SEPIABOX_RECORD_GOLDEN=1 dotnet test tests/Sepiabox.Web.Tests --filter "Category=LiveApi" --logger "console;verbosity=detailed"`. It must report `Passed: 1` (`Skipped: 1` means the key is not set); its output ends with `Model claude-haiku-5-5, <date>: <passed>/110 passed, <n> after a retry (<ids>).` and lists every miss. Only when every question passed does it rewrite `questions.recorded.json` (all 110 responses, `placeholders` removed) and print `Recorded <path>.`; otherwise it prints `Not recording: N failed, ...` and leaves the file as it was (D-076).
4. `dotnet test tests/Sepiabox.Web.Tests --filter "FullyQualifiedName~GoldenQuestions&Category!=LiveApi"` (the CI replay, now on the recorded responses) must pass.
5. `git add tests/Sepiabox.Web.Tests/TestData/questions.recorded.json`, tick the last acceptance criterion with the run's line from step 3, set S-014 to `done` in `docs/backlog/README.md` and in the Status below, commit, push, and put the run's line in the PR description.

If the run fails, keep the story `blocked` (the recorded file is unchanged) and report the misses: they are prompt or golden-set fixes for a review iteration.

### Status
- `blocked` (2026-10-21): implemented and tested (D-083); waiting for the live golden run with recording (D-054), whose six new questions are placeholders until then.

### Expected tests
- Integration (PostgreSQL): radius, minimum distance, uniqueness. bUnit: form.
