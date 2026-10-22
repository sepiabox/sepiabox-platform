## S-067 — Timeline filters and the question box, redesigned
**Epic:** Visual design
**Depends on:** S-066, S-052
**Priority:** High
**Size:** M

### Context
The owner's main complaint: the filters are native selects, a listbox and a bare checkbox (D-119). The mockups `docs/design/redesign/timeline*.html` show the replacement.

### Scope
- The filter bar becomes dropdown chips: each a `<details name="f">` whose summary reads "Year: 2019" (localized) and is tinted when set, with radios or checkboxes and counts in its popover. The form field names stay, so the server side and its tests do not change; without JavaScript an "Apply filters" button submits, with JavaScript a change submits.
- The People picker: a popover with a search box, a row per person with an avatar (the face crop when there is one, initials otherwise), a sticky "Reset / Show results" footer.
- "With me" and "On this day" become toggle pills. The question box becomes one pill-shaped field with the Ask button inside and the privacy hint under it.
- Active filter chips restyled; on screens of 600 px or less the popovers are bottom sheets.

### Out of scope
- New filters or DSL changes; the question translator (unchanged, no recording).

### Acceptance criteria
- [ ] Given the timeline with no JavaScript, when I pick a year and two people and press "Apply filters", then the same URL and results as today come back.
- [ ] Given JavaScript, when I tick a person, then the results update and the chip shows the count.
- [ ] Given 390 px, then the filters wrap with no horizontal scroll and a popover opens as a bottom sheet.
- [ ] Given `en` and `pt-BR`, then every label and chip text comes from the resources.

### Technical notes
- `<details name>` (exclusive popovers) needs a current browser (Chrome, Edge, Safari and Firefox of 2024 or later); in an older one the popovers simply do not close each other.
- Reuse the `more.js` pattern for closing on an outside click; no framework. DaVinci reviews.
- Decisions: D-050, D-119.

### Expected tests
- bUnit: the filter form posts the same field names; the chips show the active values; the people picker lists people with avatars.
