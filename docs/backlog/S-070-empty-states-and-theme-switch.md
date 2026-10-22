## S-070 — Empty states, notes and a theme switch
**Epic:** Visual design
**Depends on:** S-069
**Priority:** Low
**Size:** S

### Context
The last part of the redesign (D-119): pages without content say so with a plain sentence, and the theme follows only the system setting.

### Scope
- Illustrated empty states (SVG, no images of people) on the pages that can be empty: timeline, people, occasions, places, storage, sources, organize, export.
- Notes after an action as toast-like cards, the same on every page.
- A theme switch (system / light / dark) in Settings, remembered per browser in a cookie.

### Acceptance criteria
- [ ] Given each page with no content, in `en` and `pt-BR`, then it shows its illustrated empty state with a localized sentence.
- [ ] Given the theme switch on "dark" with a light system setting, then the app is dark, and it stays so after a reload; "system" follows the system setting again.

### Technical notes
- DaVinci reviews. Decisions: D-050, D-119.

### Expected tests
- bUnit: each empty state; the theme switch's cookie and the class on `<html>`.
