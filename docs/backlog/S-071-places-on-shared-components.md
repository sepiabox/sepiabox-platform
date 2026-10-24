## S-071 — Places
**Epic:** Visual design
**Depends on:** S-069
**Priority:** Medium
**Size:** M

### Context
DaVinci's audit of the places page(s) after S-069 put the shared head, panels and form primitives on them (D-119, D-126). This story carries only what that audit still found missing. The app is desktop first (D-126).

### Already done in S-069
The page already has the shared head, a Panel for the new-place form, saved places as cards and the danger button on the delete confirmation (S-069); the form is already on `.field` and `.form-row`.

### Scope
- The create form on the form primitives (`.field`, `.form-row`, `.form-actions`): labels above, aligned, with helper text; the "home" checkbox moved up and explained in a line of help.
- Each saved place's edit form as labelled fields in a card, not a run-on row; Delete as a danger button, not a plain link, with its confirmation as a Panel.
- The empty state (S-070's `EmptyState`) when there are no places, with the way to add one.

### Acceptance criteria
- [ ] Given the places page(s) in `en` and `pt-BR`, light and dark, at 1440 and 1920 px (and 390 px without horizontal scroll), then each item above is in place and uses the shared components.

### Technical notes
- DaVinci reviews every page. Decisions: D-050, D-119, D-126.

### Expected tests
- bUnit per page for the component classes and the new markup.
