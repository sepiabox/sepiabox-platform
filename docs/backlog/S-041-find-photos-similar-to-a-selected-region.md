## S-041 — Find photos similar to a selected region
**Epic:** Search
**Depends on:** S-017, S-023
**Priority:** Low
**Size:** S

### Context
"Em que ocasiões usei este vestido vermelho?" ("On which occasions did I wear this red dress?"): the user marks the dress in a photo and Sepiabox searches for similar photos, grouped by occasion.

### Scope
- In the detail view: draw a rectangle on the preview.
- The server crops the `preview-1280` (Magick.NET), computes the CLIP embedding of the crop and runs kNN.
- DSL: `similarToRegion: { "assetId": "<sha256>", "box": [x, y, w, h] }` (relative 0..1); results grouped by occasion.

### Out of scope
- Automatic clothing detection.

### Acceptance criteria
- [ ] Given a relative box, then the crop has the expected pixel dimensions.
- [ ] Given a box outside 0..1 or with zero area, then the API returns 400.
- [ ] Given results, then they are grouped by occasion.

### Technical notes
- Reuses the S-015 image encoder in the web app.

### Expected tests
- Unit: cropping and validation. Integration: grouping with synthetic embeddings.
