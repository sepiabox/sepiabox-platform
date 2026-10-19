## S-015 — Compute CLIP embeddings during processing
**Epic:** Search
**Depends on:** S-005, S-008
**Priority:** High
**Size:** M

### Context
"Fotos na neve" ("Photos in the snow") and "parecidas com esta" ("similar to this one") use CLIP embeddings (D-016), computed once on the PC and stored in pgvector.

### Scope
- `models/models.json` (name, URL, SHA-256, license) and `sepiabox-ingest models download` to `~/.sepiabox/models` (Windows: `%LOCALAPPDATA%\Sepiabox\models`); rejects files with a different SHA-256.
- CLIP ViT-B/32 vision: `https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/vision_model.onnx` (fill in the SHA-256 when implementing).
- Preprocessing: resize the shorter side to 224 (bicubic), center crop 224×224, CLIP normalization (mean 0.48145466, 0.4578275, 0.40821073; std 0.26862954, 0.26130258, 0.27577711).
- L2-normalized 512-d embedding; in the manifest `embeddings["clip-vit-b32"]` as base64 (float32 little-endian). Videos use the `poster`.
- Import: table `asset_embeddings` (asset_id, model, embedding `vector(512)`), HNSW index with `vector_cosine_ops`.
- ONNX Runtime on CPU; `--gpu` uses CUDA if available.

### Out of scope
- Text encoder (S-016).

### Acceptance criteria
- [ ] Given an image, then the embedding has 512 values and norm 1 ± 0.001.
- [ ] Given the same image twice, then the cosine is > 0.999.
- [ ] Given an image and its resized version, then the cosine is > 0.9.
- [ ] Given a model file with the wrong SHA-256, then the download fails with a clear message.
- [ ] Given a manifest with an embedding, when it is imported, then the row exists in `asset_embeddings`.

### Technical notes
- Packages: `Microsoft.ML.OnnxRuntime`, `Pgvector.EntityFrameworkCore`.
- Tests with the model: Integration; cache the model in CI with `actions/cache`.
- Extend `ManifestMerge` (D-036): `embeddings` merge by model key, the incoming value winning per key, so a manifest from a drive processed with another model version never drops the stored vectors.
- Decisions: D-016, D-036.

### Expected tests
- Unit: preprocessing (tensor and normalization), base64. Integration: real inference and import.
