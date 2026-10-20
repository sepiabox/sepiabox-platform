## S-018 — Detect faces during processing
**Epic:** People
**Depends on:** S-005, S-015
**Priority:** High
**Size:** M

### Context
"Fotos minhas com a Ana" ("Photos of me with Ana") requires recognizing faces. It is opt-in (biometric data) and runs on the PC (D-016).

### Scope
- `process --faces`. Models in `models.json`: YuNet `face_detection_yunet_2023mar.onnx` and SFace `face_recognition_sface_2021dec.onnx` (repository `opencv/opencv_zoo`).
- Detection on `preview-1280`: score ≥ 0.8, minimum side 40 px; 5-point alignment to 112×112 (similarity transform to the ArcFace reference points); normalized 128-d SFace embedding.
- Manifest: `facesProcessed: true` and `faces: [{ "box": [x, y, w, h] (relative 0..1), "score": 0.97, "embedding": "<base64>" }]`. Without `--faces`: `facesProcessed: false`.

### Out of scope
- Grouping into people (S-019).

### Acceptance criteria
- [x] Given a solid-color image, then 0 faces.
- [x] Given the fixture with 1 face, then 1 face with score ≥ 0.8 and a box inside the image.
- [x] Given the same fixture resized to 70%, then the cosine between the embeddings is ≥ 0.6.
- [x] Given `process` without `--faces`, then `facesProcessed` is false and there are no faces.

### Technical notes
- Fixture: a public-domain portrait (e.g. an official US government portrait on Wikimedia Commons), with source and license in `tests/.../TestData/README.md`.
- Alignment with Magick.NET (affine `Distort`) or a custom matrix.
- Extend `ManifestMerge` (D-036): `faces` come from the side with `facesProcessed: true`, the incoming one if both are, so uploading from a work folder with face detection off never erases detected faces.
- Decisions: D-016, D-036.

### Expected tests
- Unit: alignment transform. Integration (models): detection, embeddings, opt-in.
