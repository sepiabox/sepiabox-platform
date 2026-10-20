## S-056 — Ship the CLIP text model with the web in Azure
**Epic:** Search
**Depends on:** S-016, S-032
**Priority:** High
**Size:** S

### Context
Search by description (S-016) needs the CLIP text model (~250 MB, `onnx/text_model.onnx`) inside the web. The web scales from 0 to 1 replica in Azure, so the model must be in place at start, verified, and its memory and cold start must stay acceptable (D-055).

### Scope
- The web's container image carries the text model, its tokenizer files and `models.json`: downloaded once in the build with the pinned revision and SHA-256 (D-044), cached between CI runs, and verified again by the web at start.
- The web loads the model lazily on the first description search, keeps one session, and reports it in `/health` once loaded or failed.
- Container Apps settings for the web: memory raised to fit the model (1 GiB with 0.5 vCPU, or the smallest step that passes the smoke test), still 0–1 replicas.
- In Production, a missing or mismatching model is an error at start (logged once, `/health` unhealthy) and description search answers 503 with a localized message; Development keeps working without it (description search hidden).

### Out of scope
- A quantized text model (it would no longer match the stored image embeddings exactly).

### Acceptance criteria
- [ ] Given the published web image, when it is inspected (test on the publish output), then it contains the text model whose SHA-256 matches `models.json`.
- [ ] Given the web in the `Production` environment with the model present, when a description search runs, then the model loads and results come back (test with the real model, Integration).
- [ ] Given the web in `Production` with the model missing or corrupted, when it starts, then it logs one clear error, `/health` is unhealthy and description search answers 503.
- [ ] Given the generated Bicep, then the web has the chosen memory and `minReplicas: 0`.
- [ ] Given a real deploy (manual, owner acceptance in `docs/operations.md`), when the first description search runs after a cold start, then it answers within 30 s and later ones within 2 s.

### Technical notes
- Decisions: D-016, D-038, D-044, D-055.

### Expected tests
- Publish-output test for the model files; Production-configuration tests for both outcomes; Bicep test for memory.
