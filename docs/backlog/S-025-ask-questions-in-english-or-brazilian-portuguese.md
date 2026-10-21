## S-025 — Ask questions in English or Brazilian Portuguese with Claude Haiku
**Epic:** Questions
**Depends on:** S-016, S-044
**Priority:** Medium
**Size:** M

### Context
People type the way they speak, in English first ("videos from Lisbon in December 2019", "photos in the snow last summer") or in Portuguese ("vídeos de Lisboa em dezembro de 2019"), whatever the UI language. This is the heart of the MVP (D-053): ask about the imported photos and videos and get correct answers. The MVP translator covers what the DSL has by then: dates, places, kinds, sources and description search; later stories extend it (people S-021, personal places S-014, occasions S-024). Claude Haiku translates the question into the DSL; only the text leaves for the Anthropic API, never the photos (D-007, D-052).

### Scope
- `IQuestionTranslator` (the project's own interface, D-051) in `Sepiabox.Search` (D-056), so the web and the end-to-end test of S-054 share it, with one implementation using Anthropic's official C# SDK (NuGet `Anthropic`, `AnthropicClient`) against the standard Anthropic API (Messages), model `claude-haiku-5-5`, structured outputs (`output_config.format`) with the `search.v1.json` schema, `effort` low, `max_tokens` 1024 (D-052).
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
- [x] Given a fake translator that returns a DSL, when I ask a question, then the results and the chips match that DSL. *(`QuestionPageTests`, `QuestionEndpointTests`.)*
- [x] Given the built request, when it is inspected, then it uses the model `claude-haiku-5-5`, the DSL schema as the output format, and contains no images. *(`AnthropicQuestionTranslatorTests`, on the JSON the SDK sends.)*
- [x] Given the context, when the prompt is built, then it includes today's date, the sources and the catalog's cities and countries. *(`AnthropicQuestionTranslatorTests`, `QuestionEndpointTests`, `LibraryQueriesTests`.)*
- [x] Given a fake translator, when I ask the same question in English and in Portuguese with the UI in either culture, then both reach the translator unchanged and the chips are labelled in the UI culture. *(`QuestionPageTests`, `SearchChipsTests`.)*
- [ ] Given the committed golden set `tests/Sepiabox.Web.Tests/TestData/questions.golden.json` (at least 30 questions, each in English and in Brazilian Portuguese, covering dates and ranges, "on this day", places including "Lisboa" → "Lisbon", kinds, sources and descriptions, each with its expected DSL and a fixed "today"), when it runs in CI against recorded responses, then parsing and validation give the expected DSL. *(Not ticked: the set (32 questions) and the replay (`GoldenQuestionsTests`, and `GoldenQuestionsCatalogTests` comparing what the queries find) are in place and pass, but `questions.recorded.json` holds the expected queries until the live run records the model's responses.)*
- [ ] Given the same golden set, when it runs against the real Anthropic API (tests marked `Category=LiveApi`, run with the key before the story is `done`), then every question's returned DSL means the same as the expected one: both are executed against a small seeded fixture catalog and their result sets are equal (not a comparison of JSON text). A question that misses may be retried once, and the retry is recorded; the story can only be `blocked`, not `done`, if the key is missing. *(Not run: `ANTHROPIC_API_KEY` is not set in the agents' environment, and a request with a placeholder key gets 401 from `api.anthropic.com`, so the story is `blocked` (D-054). `GoldenQuestionsLiveTests` is ready: `dotnet test tests/Sepiabox.Web.Tests --filter "Category=LiveApi"`.)*
- [x] Given invalid JSON or a model refusal, when I ask a question, then I see a message and the app does not fail. *(`AnthropicQuestionTranslatorTests`, `QuestionEndpointTests`, `QuestionPageTests`.)*
- [x] Given the daily limit has been reached, when I ask a question, then the API returns 429. *(`QuestionEndpointTests`; the counter itself in `CatalogQuestionQuotaTests`.)*

### Technical notes
- Before writing code, read the Anthropic C# SDK README for the exact names (do not guess the API). Do not use assistant prefill; Haiku 5.5 does not accept it.
- Portability (D-051): only the Anthropic implementation of `IQuestionTranslator` references the `Anthropic` package; pages, endpoints and tests depend on the interface, and the client is registered in `Program.cs`. Another provider would be a second implementation.
- No real calls in CI. CI's unit filter becomes `Category!=Integration&Category!=LiveApi` (update `.github/workflows/ci.yml` and the commands in `CLAUDE.md`). The live golden run is required before `done` (D-054). The agents' environment injects the real `x-api-key` header into requests to `api.anthropic.com/v1/` (a network secret, `docs/operations.md`), so the agent never sees the key: it sets a placeholder `Anthropic:ApiKey` (for example the environment variable `Anthropic__ApiKey=injected-by-environment`), because the SDK requires a value. Before the golden run it makes one minimal call; a 401 or 403 means the secret is missing or wrong, and the story becomes `blocked` with that reason, never a silent failure or a skipped run. Never log request headers. Production is unchanged: the web reads `Anthropic:ApiKey` from Key Vault (S-058). The run's result (pass count, retries, model, date) goes in the PR description. Recorded responses for CI are refreshed from that run.
- Cost: Anthropic bills the API separately from Azure, outside D-008's €50/month Azure ceiling, under its own spend limit (D-052).
- The Anthropic API key is a human prerequisite (`docs/operations.md`); without it the question box says questions are not configured, and the rest of the app works.
- Decisions: D-007, D-017, D-020, D-050, D-051, D-052, D-054, D-056.

### Status
- `blocked` (2026-10-20): the required live golden run needs `ANTHROPIC_API_KEY` in the agents' environment (docs/operations.md, prerequisites); everything else is implemented and tested (D-073). Once the key is set: run the `LiveApi` test with `SEPIABOX_RECORD_GOLDEN=1`, commit the recorded responses, tick the last two criteria and set the story `done`.

### Expected tests
- Unit: prompt and request construction, refusal/invalid JSON handling, daily limit. Integration: endpoint with a fake translator.
