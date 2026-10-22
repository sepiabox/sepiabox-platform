## S-064 — Show where an index-only original lives
**Epic:** Web app
**Depends on:** S-063, S-011, S-044
**Priority:** Medium
**Size:** S

### Context
An asset processed in index-only mode (S-063, D-088) has no original in Sepiabox, so "Download the original" cannot work for it. The detail page shows the larger preview and says where the original is. It links there when the source gives an address (a Google Photos or Instagram link from the export); otherwise a sentence says where the original is kept ("Original on the drive 'Life'", "Original in iCloud Photos"), which Sepiabox cannot open from here.

### Scope
- **Larger previews.**
  - The detail page shows the largest photo preview the asset has (`preview-2048`, else `preview-1280`) and plays the largest video version (`proxy-1080p`, else `proxy-720p`).
  - The timeline keeps `thumb-256`.
  - `DerivativeNames` serves the two new names through the existing media endpoints, with the same SAS rules (D-031).
- **Links at the source, always.** Every source with a `url` gets a link, whether or not the original is stored: next to "Download the original" when it is. The link reads "Open in Google Photos" / "Open on Instagram" by the source's type, or "Open at the source" for any other type. It opens in a new tab with `rel="noopener noreferrer"`, and only `https://` addresses are ever rendered as links.
- **Instead of "Download the original"**, when the catalog says the original is not stored (`original_stored = false`), each source without a link gets one line, in the sources' existing order:
  - **A local source** (the drive) gets "Original on the drive '{name}': {path}". The page cannot reach it.
  - **A cloud source** (Google Drive, OneDrive) gets its line from S-038/S-039, which come later and add their "Open original" through the server to this list.
  - **An export source without a `url`** gets one sentence by kind:
    - `apple-photos`: "Original in Apple Photos / iCloud Photos; it can't be opened from here."
    - `facebook`: "Original on Facebook; it can't be opened from here."
    - any other kind: "Original in {export name}; it can't be opened from here."
- **Download endpoint.** `GET photos/{id}/original` answers 404 with a problem body saying the original is kept at its source, rather than signing a link to a blob that does not exist. The endpoint itself must check `original_stored = false` and answer 404 (hiding the link on the page is not enough: S-063 left `MediaEndpoints.OriginalAsync` as it was).
- **Choosing the derivatives.** S-063 only made the web serve `preview-2048` and `proxy-1080p` (`DerivativeNames`). `GetAssetAsync`'s `previewOrder` and `HasProxy` still look for `preview-1280`, `poster` and `proxy-720p`, so this story adds the larger names (largest first) there. The "Download the original" link and the preview choice change together.
- **API.** The API's asset detail, if it shows sources, includes each source's `url` and the asset's `originalStored`.
- **Text.** All new text in English and Brazilian Portuguese (D-050), for example:

  | English | pt-BR |
  |---|---|
  | "Open in Google Photos" | "Abrir no Google Fotos" |
  | "Open on Instagram" | "Abrir no Instagram" |
  | "Original on the drive 'Life'" | "Original no HD 'Life'" |
  | "Original in Apple Photos / iCloud Photos; it can't be opened from here." | "Original no Fotos da Apple / Fotos do iCloud; não dá para abrir daqui." |

  The wording is checked by the resource tests of D-058 (every key translated, the same placeholders, no European Portuguese wording).

### Out of scope
- Opening a local original through the browser (the drive is on the family's PC).
- Fetching anything from Google Photos, iCloud or Instagram (D-053, D-088).
- Switching a source between modes (S-063 covers adding originals later; removing them is out of scope there).

### Acceptance criteria
- [x] Given a photo with `preview-2048` and `preview-1280`, when its detail page renders, then the image is `preview-2048`; given a video with `proxy-1080p`, then the player's source is `photos/{id}/proxy-1080p`.
- [x] Given an index-only photo whose `google-takeout` source has the url `https://photos.google.com/photo/AF1Qip-example`, then the page shows "Open in Google Photos" linking there in a new tab with `rel="noopener noreferrer"`, and no "Download the original".
- [x] Given an index-only photo whose only source is local, with the name "Life" and the path `2019/IMG_0001.jpg`, then the page says "Original on the drive 'Life'" with the path, and shows no link and no download.
- [x] Given an index-only photo from `apple-photos`, then the page says it is stored in Apple Photos / iCloud Photos and can't be opened from here.
- [x] Given a source url that is not `https://` (for example `javascript:alert(1)` in a hand-edited catalog row), then no link is rendered.
- [x] Given an index-only asset, when `photos/{id}/original` is requested, then the answer is 404 with the message; given a stored original, the answer is unchanged (a redirect to a short-lived SAS link).
- [x] Given a photo whose original is stored and also has an export source with a url, then "Download the original" stays, and the source's link is shown too.
- [x] Given the pt-BR culture, then every new text is in Brazilian Portuguese.

### Technical notes
- The `url` comes from the catalog (S-063), which comes from the manifest. Treat it as untrusted text: render it only as an `href` with the `https` scheme checked, through `Uri.TryCreate` (absolute, `Uri.UriSchemeHttps`). Never put it in a redirect from the server.
- Source type labels come from one map (type → resource key), so S-046 to S-049 only add entries, and S-038/S-039 add the cloud sources' line.
- Decisions: D-023, D-031, D-050, D-053, D-058, D-088.

### Expected tests
- **bUnit:**
  - each kind of line (link, local drive, export without a link), and a link next to "Download the original";
  - the scheme check;
  - the larger preview and video chosen;
  - pt-BR text.
- **Integration (PostgreSQL + Azurite):**
  - the detail page and the 404 on `photos/{id}/original` for an index-only asset imported from a manifest;
  - an asset with a stored original keeps its download.
