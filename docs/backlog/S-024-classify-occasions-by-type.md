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
- [ ] Given synthetic embeddings aligned with "wedding" in 60% of the photos, when the occasion is classified, then the type is `wedding`.
- [ ] Given no label above the threshold in 30% of the photos, when the occasion is classified, then the type stays empty.
- [ ] Given a 2-day occasion 300 km from the home place, when it is classified, then it has the type `trip`.
- [ ] Given `eventTypes=[wedding]`, when occasions are searched, then only occasions of that type are returned.
- [ ] Given 4 occasions of type `wedding` in 2026, when I count `events` with `eventTypes=[wedding]` and `years=[2026]`, then `total` is 4.

### Technical notes
- Text embeddings computed once (S-016) and kept in memory; tests use a fake encoder.
- Decisions: D-019, D-050.

### Expected tests
- Unit: voting, thresholds, trip. Integration: filter.
