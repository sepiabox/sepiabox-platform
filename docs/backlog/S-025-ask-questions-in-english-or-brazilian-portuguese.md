## S-025 — Ask questions in English or Brazilian Portuguese with Claude Haiku
**Epic:** Questions
**Depends on:** S-016, S-021, S-024, S-014
**Priority:** Medium
**Size:** M

### Context
People type the way they speak, in English first ("photos of me with Ana in the snow in 2019") or in Portuguese ("fotos minhas com a Ana na neve em 2019"), whatever the UI language. Claude Haiku translates the question into the DSL; only the text leaves Azure France Central, never the photos (D-007).

### Scope
- `IQuestionTranslator` using Anthropic's official C# SDK (`Anthropic.Foundry`, `AnthropicFoundryClient`), model `claude-haiku-5-5`, structured outputs with the `search.v1.json` schema, `effort` low, `max_tokens` 1024.
- Stable system prompt (DSL documentation, examples) with prompt caching; variable context at the end: today's date, people's names, personal places, sources, occasion types.
- Questions in English and Portuguese (Brazilian or European spelling) are understood whatever the UI culture; the system prompt's examples cover both. Place names are mapped to the stored GeoNames names ("Lisboa" → "Lisbon"), and occasion types to the English keys of S-024 ("casamentos" → `wedding`).
- Visual terms translated into English in `visual.text` (D-017).
- UI: question box on `/`; shows the interpreted filters as editable chips (labels in the current culture), plus the results.
- Daily limit per user (200 by default) → friendly message (localized, D-050) and 429; records tokens used.
- `stop_reason` "refusal" or invalid JSON → friendly message, no exception.
- Configuration `Foundry:Resource`, `Foundry:ApiKey` (Key Vault in production).

### Out of scope
- Answers with counts (S-026).

### Acceptance criteria
- [ ] Given a fake translator that returns a DSL, when I ask a question, then the results and the chips match that DSL.
- [ ] Given the built request, when it is inspected, then it uses the model `claude-haiku-5-5`, the DSL schema as the output format, and contains no images.
- [ ] Given the context, when the prompt is built, then it includes the names of the existing people and places.
- [ ] Given a fake translator, when I ask the same question in English and in Portuguese with the UI in either culture, then both reach the translator unchanged and the chips are labelled in the UI culture.
- [ ] Given invalid JSON or a model refusal, when I ask a question, then I see a message and the app does not fail.
- [ ] Given the daily limit has been reached, when I ask a question, then the API returns 429.

### Technical notes
- Before writing code, read the Anthropic C# SDK README for the exact names (do not guess the API). Do not use assistant prefill.
- No real calls in CI; an optional manual test with a real key.
- The Foundry deployment is a human prerequisite (`docs/operations.md`).
- Decisions: D-007, D-017, D-020.

### Expected tests
- Unit: prompt and request construction, refusal/invalid JSON handling, daily limit. Integration: endpoint with a fake translator.
