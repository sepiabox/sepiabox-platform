## S-016 — Search photos by description
**Epic:** Search
**Depends on:** S-013, S-015
**Priority:** High
**Size:** M

### Context
The CLIP text encoder turns "snow" into a vector comparable with the photo embeddings. Search is in English (D-017); the question translator (S-025) turns visual terms asked in Portuguese into English.

### Scope
- Text model `Xenova/clip-vit-base-patch32` (`onnx/text_model.onnx`, `vocab.json`, `merges.txt`) in `models.json`.
- CLIP BPE tokenizer in C# (lowercase, CLIP regex, `</w>` suffix, start 49406, end 49407, length 77).
- DSL: `visual: { "text": "snow", "minScore": 0.2 }`; sorts by similarity, combinable with the other filters.
- UI: "Describe (in English)" box in the search bar ("Descrever (em inglês)" in pt-BR), hidden in every environment while the text model is missing or failed to load (S-056 adds Production's health reporting and the API's 503).

### Out of scope
- Translation from Portuguese (S-025).

### Acceptance criteria
- [x] Given "a photo of a cat", when I tokenize it, then I get `[49406, 320, 1125, 539, 320, 2368, 49407]` followed by padding.
- [ ] Given a text, then the embedding has 512 values and norm 1. *(Pinned with the owner's SHA-256; verified by CI's integration job, which downloads the model, not in the agents' sandbox.)*
- [x] Given assets with known synthetic embeddings, when I search with a vector close to one of them, then that one comes first.
- [x] Given `visual` with `years=[2019]`, then only assets from 2019 come back, sorted by similarity.
- [x] Given the web without the text model, when the timeline renders, then the description box is not shown and the other filters work.

### Technical notes
- Try `Microsoft.ML.Tokenizers` (BPE with end-of-word suffix); if it does not reproduce the reference ids, implement CLIP's BPE (~150 lines).
- Where it lives (D-055): `ClipTokenizer` and `ClipTextEncoder` go in a new small library `Sepiabox.Search` (ONNX Runtime only, no Magick.NET or FFmpeg), which the web references, keeping D-038; the model verification shared with the image encoder (`models.json`, SHA-256, `ModelSession`, D-044) moves there and `Sepiabox.Pipeline` references it. Singleton service with lazy loading in the web. Shipping the model to the Azure web is S-056; locally the web reads it from the folder `models download` fills.
- Portability (D-051): the kNN query (`<=>` with the HNSW index) is pgvector-specific and lives in a provider-specific class (`PgvectorSimilarity`) behind the search service.
- The text model must come from the same repository revision as the image model in `models/models.json` (`Xenova/clip-vit-base-patch32` at `d15189d7028b43f1d3e65039190477f6af591c2a`, D-044), so both encoders share one embedding space.
- Decisions: D-016, D-017, D-020, D-038, D-051, D-055.

### Expected tests
- Unit: tokenizer with reference ids. Integration: text inference and kNN query.
