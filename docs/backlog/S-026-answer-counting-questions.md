## S-026 — Answer counting questions
**Epic:** Questions
**Depends on:** S-025
**Priority:** Medium
**Size:** M

### Context
"How many weddings did we go to this year?" ("Quantos casamentos fomos este ano?") and "Who did I take the most photos with in 2025?" ("Com quem mais tirei fotos em 2025?") ask for numbers, not grids.

### Scope
- DSL: `aggregate: { "count": "events" | "assets", "groupBy": null | "person" | "year" | "eventType", "top": 10 }`.
- The executor returns `{ total, groups: [{ key, count }] }`.
- UI: answer card (big number + a sentence in the current culture, e.g. "4 weddings this year" / "4 casamentos este ano", with plural forms from the resources) or a ranking table, with a link to the items.
- Counting examples in the translator prompt, in English and Portuguese.

### Out of scope
- Byte sums (S-028).

### Acceptance criteria
- [ ] Given 4 occasions of type `wedding` in 2026, when I count `events` with `eventTypes=[wedding]` and `years=[2026]`, then `total` is 4.
- [ ] Given photos with several people in 2025, when I group by person with `top=3`, then the 3 with the most photos are returned, in order.
- [ ] Given a counting answer, when it is displayed, then the card shows the number and the sentence, in English and in pt-BR.

### Technical notes
- Extends `search.v1.json`, the executor and the prompt.

### Expected tests
- Integration (PostgreSQL): counts and groupings. bUnit: answer card.
