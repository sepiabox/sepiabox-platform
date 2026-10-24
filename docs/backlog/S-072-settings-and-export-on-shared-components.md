## S-072 — Settings and Export
**Epic:** Visual design
**Depends on:** S-069
**Priority:** Medium
**Size:** M

### Context
DaVinci's audit of the settings, export page(s) after S-069 put the shared head, panels and form primitives on them (D-119, D-126). This story carries only what that audit still found missing. The app is desktop first (D-126).

### Already done in S-069
Both pages already use the shared head and Panels; the face-recognition text no longer mentions a command-line flag (EN and pt-BR); "Turn off and delete face data" and its confirmation are danger buttons; the export request button is no longer stretched and the pt-BR "a app" is "o app" (S-069).

### Scope
- Section navigation on Settings (faces, export, and whatever comes next) so the page is not one long column.
- A danger zone: "Turn off and delete face data" and its confirmation in their own marked Panel, apart from the harmless settings.
- The face-deletion history as rows (date, who, what), not a bulleted list.
- On Export, the "include face data" checkbox next to its biometric warning, not above a separate hint.

### Acceptance criteria
- [ ] Given the settings, export page(s) in `en` and `pt-BR`, light and dark, at 1440 and 1920 px (and 390 px without horizontal scroll), then each item above is in place and uses the shared components.

### Technical notes
- DaVinci reviews every page. Decisions: D-050, D-119, D-126.

### Expected tests
- bUnit per page for the component classes and the new markup.
