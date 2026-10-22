## S-066 — Design tokens, base controls and the header
**Epic:** Visual design
**Depends on:** S-013
**Priority:** High
**Size:** M

### Context
The owner finds the app plain and dated (D-119). DaVinci's audit ranks the base controls, the flat hierarchy and the missing surfaces first. The new tokens and components are in `docs/design/redesign/css/` (`tokens.css`, `components.css`), with mockups and before/after screenshots beside them.

### Scope
- Replace the `:root` block of `wwwroot/app.css` with the tokens of `docs/design/redesign/css/tokens.css`: the light and dark palettes (`prefers-color-scheme`), the type scale (12/14/16/18/22/28/36), the 4-pt spacing, radii, shadows and focus rings. Brand colours and the three self-hosted OFL fonts stay.
- Base controls: buttons (primary, secondary, ghost), inputs and selects (40 px, borders at 3:1 or more), checkboxes and radios, cards, and the note as a toast-like card.
- The header: pill links with an active state, the segmented EN/PT switch, sticky; the "More" menu (D-102) keeps working. Anchored targets get `scroll-margin-top` for the sticky header (#106).
- Reduced motion turns transitions off.

### Out of scope
- New filter controls (S-067), the photo grid and detail (S-068), the other pages' layouts (S-069).
- A theme switch in the app (system setting only; S-069 may add one).

### Acceptance criteria
- [ ] Given any page in light and in dark, then every text and control pair measured in DaVinci's table passes WCAG AA, and control borders reach 3:1.
- [ ] Given the header at 1024 px and 390 px in `en` and `pt-BR`, then it is one row with no horizontal scroll, the current page is marked, and the language switch works without JavaScript.
- [ ] Given keyboard use, then every interactive element shows the focus ring.
- [ ] Existing pages keep working with no markup change other than the header.

### Technical notes
- CSS only, plus the header markup; no new dependency. DaVinci reviews renders at 1024/390, both languages, both themes.
- Decisions: D-050, D-102, D-119.

### Expected tests
- bUnit: the header's active state and language switch. A contrast check of the token pairs (a unit test over the token values).
