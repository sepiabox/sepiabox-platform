## S-011 — Watch videos on the detail page
**Epic:** Web app
**Depends on:** S-006, S-010
**Priority:** Medium
**Size:** S

### Context
Videos have a poster and a 720p version (S-006); the web has to show and play them.

### Scope
- Grid: video poster with a play icon and duration (`m:ss`).
- Video detail: `<video controls>` with a SAS `src` for `proxy-720p.mp4` and a SAS `poster`.
- No proxy: message "Vídeo ainda em processamento" ("Video still processing").

### Out of scope
- Transcoding on the web.

### Acceptance criteria
- [ ] Given a video asset with a proxy, when I open the detail page, then there is a `<video>` with a SAS `src` for the proxy and a `poster`.
- [ ] Given a 3 s video, when the grid renders, then the card shows "0:03".
- [ ] Given a video without a proxy, when I open the detail page, then I see "Vídeo ainda em processamento".

### Technical notes
- Components `VideoPlayer.razor`, `MediaCard.razor`.

### Expected tests
- bUnit: video rendering, duration, message without a proxy.
