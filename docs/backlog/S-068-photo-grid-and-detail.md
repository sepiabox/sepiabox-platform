## S-068 — Photo grid and photo detail, redesigned
**Epic:** Visual design
**Depends on:** S-066, S-041, S-052, S-064
**Priority:** Medium
**Size:** M

### Context
The detail page lists faces as "Unnamed" links and a long storage path (D-119, the owner's screenshot). Mockups: `docs/design/redesign/detail*.html`.

### Scope
- Grid: rounded tiles, hover and focus overlay with the date, a duration badge on videos, month headings with counts; 3 columns on phones.
- Detail: the photo on a dark stage with previous/next; a side panel of cards: date, place and camera; "Download the original" and "Find similar"; **people as face thumbnails** (named ones ringed, unnamed ones as a dashed "Add a name"); albums as pills; "Stored on" as a compact card with the path truncated and a "Show full path" disclosure.
- Occasion and person cards in the same pattern where they list photos.
- If #107 is merged, its "Ignore this face" actions live on the face thumbnails.

### Out of scope
- New actions beyond those that exist when it is built.

### Acceptance criteria
- [ ] Given a photo with two named and six unnamed faces, then the panel shows eight thumbnails and no "Unnamed" text list.
- [ ] Given a long storage path, then it is truncated with "Show full path".
- [ ] Given keyboard use, then previous/next and every thumbnail are reachable, with focus visible on the dark stage.
- [ ] Given 390 px and `pt-BR`, then no horizontal scroll.

### Technical notes
- Face thumbnails are cropped from the existing thumbnails, as the People page does. DaVinci reviews.
- Decisions: D-019, D-050, D-119.

### Expected tests
- bUnit: the face thumbnails, the stored-on card, the previous/next links.
