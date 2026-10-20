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
- [ ] Given 4 videos and 6 photos in 2019, when I count `assets` with `kinds=[video]` and `years=[2019]`, then `total` is 4.
- [ ] Given photos in Lisbon across several years, when I group by year with `cities=[Lisbon]` and `top=3`, then the 3 years with the most photos are returned, in order.
- [ ] Given the question "how many videos did we take in 2019?" and a fake translator returning that count, when it is asked, then the answer card shows 4.
- [ ] Given a counting answer, when it is displayed, then the card shows the number and the sentence, in English and in pt-BR.
- [ ] Given a count with a `visual` filter, when it is executed, then no number is returned and the UI shows the results with the explanation.
- [ ] Given 3 undated assets and a count with `years=[2019]`, when it is displayed, then the card shows "3 items have no date" next to the number.
- [ ] Given the golden set of S-025, when at least 10 counting questions (English and Brazilian Portuguese: by year, kind, place, source, "on this day") with their expected DSL are added, then they pass in CI (recorded) and in the required live run before `done`, compared by meaning as in S-025: the expected and returned DSL are counted against the seeded fixture catalog and the numbers must be equal (one recorded retry per question).

### Technical notes
- Extends `search.v1.json`, the executor and the prompt.
- Portability (D-051): counts and groupings use plain EF Core `GroupBy` in the search service; nothing provider-specific.
- Decisions: D-020, D-051, D-053, D-054.

### Expected tests
- Integration (PostgreSQL): counts and groupings. bUnit: answer card.
