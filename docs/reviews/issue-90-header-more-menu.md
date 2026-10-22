# Review of the fix for issue #90 — Group the header's less used pages under "More"

Process: [docs/process.md](../process.md). Edison implemented it while Sprint 14's track A finished its reviews. DaVinci reviewed it with measured renders and `more.js` run in headless Chromium, and Atlas and Spock reviewed it after him.

## Iteration 1

Diff: `5300777...84fd202`.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P2 | DaVinci | The new `<details class="more">` picked up the existing `.more { margin-top: 32px }` rule for "Show more": "More" sat 16 px low and the header was 81 px tall | ✅ Yes (`28e4185`) | Header classes renamed to `nav-more*`; a test checks the header has no `.more` |
| P2 | DaVinci | Even so, pt-BR (and a long address) wrapped to two rows at 1024 px | ✅ Yes (`28e4185`) | `.user` capped at 11 rem with an ellipsis at every width; the full address stays in `title` |
| P3 | DaVinci | Tabbing out left the menu open over the page | ✅ Yes (`28e4185`) | Close on `focusout` outside the menu |
| P3 | DaVinci | Escape pulled focus to the header from a field elsewhere | ✅ Yes (`28e4185`) | Escape acts only when focus is inside the menu |
| P4 | DaVinci | No accessible hint of the current page inside "More" | ✅ Yes (`28e4185`) | An `aria-label` on the summary |

## Iteration 2

DaVinci re-measured `28e4185`: **one row (59 px) at 1024 px in EN and pt-BR, with a short and a 55-character address**; 166 px closed at 390 px, with nothing clipped and no horizontal scroll. He approved it. Atlas (the whole Web suite, 1365 passed) and Spock (17 mutations, 16 killed) found nothing above P4. The P4s were done in `daaf5d6`, which Mozart checked against the diff and CI:
- the summary's accessible name starts with its visible text ("More: Places, current page");
- the panel has `tabindex="-1"`;
- `aria-current` matching ignores case, as Blazor's routes do;
- a `focusout` during a pointer press inside the menu is ignored (for Safari);
- addresses with `#` are tested.

`more.js` has no automated test (the repository has no JavaScript test setup); DaVinci's browser runs are its check, and the Safari change was not run in Safari.
