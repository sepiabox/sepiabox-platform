## S-074 — Person, Merge and People
**Epic:** Visual design
**Depends on:** S-069
**Priority:** Medium
**Size:** M

### Context
DaVinci's audit of the person, merge, move, delete and people page(s) after S-069 put the shared head, panels and form primitives on them (D-119, D-126). This story carries only what that audit still found missing. The app is desktop first (D-126).

### Already done in S-069
People is done: named people first, a "Needs a name" section with a callout that counts them, person cards on `PersonAvatar`. The person page has the shared head, a Panel for the corrections and counted section titles; the delete and merge confirmations are danger buttons (S-069).

### Scope
- On the person page, the rename, "this is me" and merge forms in one card with labelled rows (`.field`, `.form-row`, `.form-actions`).
- The floating "Delete face data" link moved into the card as a danger action; the "Not this person", "Not a face" and "Move" buttons styled as small secondary buttons on each face.
- The move, merge and delete pages: a Panel around the explanation and the cards, and danger buttons on the confirmations (merge keeps main's version from #126; only wrap it in `PageHead` and `Panel`).

### Acceptance criteria
- [ ] Given the person, merge, move, delete and people page(s) in `en` and `pt-BR`, light and dark, at 1440 and 1920 px (and 390 px without horizontal scroll), then each item above is in place and uses the shared components.

### Technical notes
- DaVinci reviews every page. Decisions: D-050, D-119, D-126.

### Expected tests
- bUnit per page for the component classes and the new markup.
