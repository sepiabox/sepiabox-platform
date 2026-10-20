## S-056 — Ship the CLIP text model with the web in Azure
**Epic:** Search
**Depends on:** S-016, S-032
**Priority:** High
**Size:** S

### Context
Search by description (S-016) needs the CLIP text model (~250 MB, `onnx/text_model.onnx`) inside the web. The web scales from 0 to 1 replica in Azure, so the model must be in place at start, verified, and its memory and cold start must stay acceptable (D-055).

### Scope
- The web's container image carries the text model, its tokenizer files and `models.json` in a **stable layer** of their own: a small base image `sepiabox-web-models` (or a `COPY` of the model folder before the app's files), rebuilt only when `models.json` changes, so app releases reuse the layer and the registry stores the model once. Downloaded in the build with the pinned revision and SHA-256 (D-044), cached between CI runs, and verified again by the web at start.
- The web loads the model lazily on the first description search and keeps one session. Its health check reports **Degraded** (never Unhealthy) when the model is missing or broken; `/health` answers 200 for Degraded and `/alive` ignores it, so the Container Apps probes keep the web ready and sign-in, timeline and filters keep working (D-027, D-055).
- Container Apps settings for the web: memory raised to fit the model (1 GiB with 0.5 vCPU, or the smallest step that passes the smoke test), still 0–1 replicas.
- In Production, a missing or mismatching model is logged once as an error at start, the health check is Degraded, and the description search API answers 503 with a localized message; in every environment the UI hides description search while the model is unavailable (S-016).

### Out of scope
- A quantized text model (it would no longer match the stored image embeddings exactly).

### Acceptance criteria
- [ ] Given the published web image, when it is inspected (test on the publish output), then it contains the text model whose SHA-256 matches `models.json`.
- [ ] Given the web in the `Production` environment with the model present, when a description search runs, then the model loads and results come back (test with the real model, Integration).
- [ ] Given the web in `Production` with the model missing or corrupted, when it starts, then it logs one clear error, the model health check is Degraded, `/health` and `/alive` answer 200 (the web stays ready), pages load, and the description search API answers 503.
- [ ] Given two image builds of different app commits with the same `models.json`, when their layers are compared, then the model layer has the same digest (a CI check on the built images).
- [ ] Given the generated Bicep, then the web has the chosen memory and `minReplicas: 0`.
- [ ] Given a real deploy (manual, owner acceptance in `docs/operations.md`), when the first description search runs after a cold start, then it answers within 30 s and later ones within 2 s.

### Technical notes
- Follow-ups from the S-016 review: warm the model at start-up in Production (load it, and embed one short text, before the web reports ready) so the first description search after a cold start does not pay for loading; measure the web's memory after the model is loaded and before raising or keeping 1 GiB.
- Registry growth is capped by S-034's `acr purge --keep 5`; the shared model layer is stored once.
- Decisions: D-016, D-027, D-038, D-044, D-055.

### Expected tests
- Publish-output test for the model files; Production-configuration tests for both outcomes; Bicep test for memory.
