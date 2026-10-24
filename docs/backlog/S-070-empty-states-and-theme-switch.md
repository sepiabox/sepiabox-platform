## S-070 — Empty states, notes and a theme switch
**Epic:** Visual design
**Depends on:** S-069, S-071, S-072, S-073, S-074, S-075, S-076
**Priority:** Low
**Size:** S

### Context
The last part of the redesign (D-119): pages without content say so with a plain sentence, and the theme follows only the system setting.

### Scope
- The `EmptyState` component exists (S-069). It is put on the timeline (an empty library, and no matches with a "Clear filters" action), people, storage, sources (its action is the Connect button) and export. Places has its own (S-071, "Add your home"); occasions and organize get theirs in S-075.
- Loading skeletons (`Skeleton`, grid or lines, pulsing unless reduced motion is asked for) replace the "Loading…" lines on the timeline, storage and sources.
- Notes after an action: `Notice` has its four variants (S-069); only the toast-like placement and dismissal are left.
- A theme switch (system / light / dark) in Settings, remembered per browser in a cookie (taken by another implementer).

### Acceptance criteria
- [ ] Given each page with no content, in `en` and `pt-BR`, then it shows its illustrated empty state with a localized sentence.
- [ ] Given the theme switch on "dark" with a light system setting, then the app is dark, and it stays so after a reload; "system" follows the system setting again.

### Technical notes
- DaVinci reviews. Decisions: D-050, D-119.

### Expected tests
- bUnit: each empty state; the theme switch's cookie and the class on `<html>`.
