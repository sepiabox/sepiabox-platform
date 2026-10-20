## S-025 — Ask questions in English or Brazilian Portuguese with Claude Haiku
**Epic:** Questions
**Depends on:** S-016, S-044
**Priority:** Medium
**Size:** M

### Context
People type the way they speak, in English first ("videos from Lisbon in December 2019", "photos in the snow last summer") or in Portuguese ("vídeos de Lisboa em dezembro de 2019"), whatever the UI language. This is the heart of the MVP (D-053): ask about the imported photos and videos and get correct answers. The MVP translator covers what the DSL has by then: dates, places, kinds, sources and description search; later stories extend it (people S-021, personal places S-014, occasions S-024). Claude Haiku translates the question into the DSL; only the text leaves for the Anthropic API, never the photos (D-007, D-052).

### Scope
- `IQuestionTranslator` (the web's own interface, D-051) with one implementation using Anthropic's official C# SDK (NuGet `Anthropic`, `AnthropicClient`) against the standard Anthropic API (Messages), model `claude-haiku-5-5`, structured outputs (`output_config.format`) with the `search.v1.json` schema, `effort` low, `max_tokens` 1024 (D-052).
- Stable system prompt (DSL documentation, examples) with prompt caching; variable context at the end: today's date, the sources and the cities and countries in the catalog. The context builder is extensible, so later stories add their names (people, personal places, occasion types) with their DSL filters.
- Questions in English and Portuguese (Brazilian or European spelling) are understood whatever the UI culture; the system prompt's examples cover both. Place names are mapped to the stored GeoNames names ("Lisboa" → "Lisbon").
- Visual terms translated into English in `visual.text` (D-017).
- UI: question box on `/`; shows the interpreted filters as editable chips (labels in the current culture), plus the results.
- Daily limit per user (200 by default) → friendly message (localized, D-050) and 429; records tokens used.
- `stop_reason` "refusal" or invalid JSON → friendly message, no exception.
- Configuration `Anthropic:ApiKey` (user secrets locally, Key Vault secret `Anthropic--ApiKey` in production, read through `IConfiguration`).

### Out of scope
- Answers with counts (S-026).
- People, personal places and occasions in questions: each of S-021, S-014 and S-024 extends the translator, its prompt examples and its context when it adds its filter. Until then, a question that needs them is answered with the filters the DSL has, and the chips show what was understood.

### Acceptance criteria
- [ ] Given a fake translator that returns a DSL, when I ask a question, then the results and the chips match that DSL.
- [ ] Given the built request, when it is inspected, then it uses the model `claude-haiku-5-5`, the DSL schema as the output format, and contains no images.
- [ ] Given the context, when the prompt is built, then it includes today's date, the sources and the catalog's cities and countries.
- [ ] Given a fake translator, when I ask the same question in English and in Portuguese with the UI in either culture, then both reach the translator unchanged and the chips are labelled in the UI culture.
- [ ] Given a set of reference questions in English and Portuguese (dates, "on this day", places, kinds, sources, descriptions) with their expected DSL, when they are checked in the optional manual test against the real API, then the DSL matches; the same set runs in CI against recorded responses to check parsing.
- [ ] Given invalid JSON or a model refusal, when I ask a question, then I see a message and the app does not fail.
- [ ] Given the daily limit has been reached, when I ask a question, then the API returns 429.

### Technical notes
- Before writing code, read the Anthropic C# SDK README for the exact names (do not guess the API). Do not use assistant prefill; Haiku 5.5 does not accept it.
- Portability (D-051): only the Anthropic implementation of `IQuestionTranslator` references the `Anthropic` package; pages, endpoints and tests depend on the interface, and the client is registered in `Program.cs`. Another provider would be a second implementation.
- No real calls in CI; an optional manual test with a real key.
- Cost: Anthropic bills the API separately from Azure, outside D-008's €50/month Azure ceiling, under its own spend limit (D-052).
- The Anthropic API key is a human prerequisite (`docs/operations.md`); without it the question box says questions are not configured, and the rest of the app works.
- Decisions: D-007, D-017, D-020, D-050, D-051, D-052.

### Expected tests
- Unit: prompt and request construction, refusal/invalid JSON handling, daily limit. Integration: endpoint with a fake translator.
