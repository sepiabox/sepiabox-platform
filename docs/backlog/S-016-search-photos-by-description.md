## S-016 — Search photos by description
**Epic:** Search
**Depends on:** S-013, S-015
**Priority:** High
**Size:** M

### Context
The CLIP text encoder turns "snow" into a vector comparable with the photo embeddings. Search is in English (D-017); the question translator (S-025) handles Portuguese.

### Scope
- Text model `Xenova/clip-vit-base-patch32` (`onnx/text_model.onnx`, `vocab.json`, `merges.txt`) in `models.json`.
- CLIP BPE tokenizer in C# (lowercase, CLIP regex, `</w>` suffix, start 49406, end 49407, length 77).
- DSL: `visual: { "text": "snow", "minScore": 0.2 }`; sorts by similarity, combinable with the other filters.
- UI: "Descrever (em inglês)" ("Describe (in English)") box in the search bar.

### Out of scope
- Translation from Portuguese (S-025).

### Acceptance criteria
- [ ] Given "a photo of a cat", when I tokenize it, then I get `[49406, 320, 1125, 539, 320, 2368, 49407]` followed by padding.
- [ ] Given a text, then the embedding has 512 values and norm 1.
- [ ] Given assets with known synthetic embeddings, when I search with a vector close to one of them, then that one comes first.
- [ ] Given `visual` with `years=[2019]`, then only assets from 2019 come back, sorted by similarity.

### Technical notes
- Try `Microsoft.ML.Tokenizers` (BPE with end-of-word suffix); if it does not reproduce the reference ids, implement CLIP's BPE (~150 lines).
- `Sepiabox.Pipeline/Clip/ClipTextEncoder.cs`, `ClipTokenizer.cs`. Singleton service with lazy loading in the web app.
- Decisions: D-016, D-017, D-020.

### Expected tests
- Unit: tokenizer with reference ids. Integration: text inference and kNN query.
