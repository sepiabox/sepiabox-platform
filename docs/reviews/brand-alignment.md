# Review of the brand alignment

Process: [docs/process.md](../process.md). Atlas knows the project; Spock sees only the diff. Not a backlog story: the web app, `sepiabox-site` and `sepiabox-brand` brought in line with the owner's approved brand design (wordmark in two colours at Bricolage Grotesque 800, the simplified favicon, the board's supporting tones). The three repositories share the branch `fix/brand-alignment`; this record covers all three, since `sepiabox-site` has no `docs/reviews/`.

## Iteration 1

Diff: `main...fix/brand-alignment` at `4e155f0` (platform), `de64148` (site), `d0aeb1b` (brand). Atlas and Spock found nothing above P4.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P4 | Spock | The global `a:hover` turns "box" in the logo blue | ✅ Yes (iteration 2) | `.brand:hover` keeps Developer, in the app and the site |
| P4 | Spock | `.ask button:hover` darkens the disabled ask button too | ✅ Yes (iteration 2) | `:hover:not(:disabled)`; disabled Cyanotype buttons at half opacity with a not-allowed cursor |
| P4 | Spock+Atlas | The favicon's dark corners vanish on dark browser tabs | ✅ Yes (iteration 2) | `prefers-color-scheme: dark` style in `sepiabox-brand/assets/symbol-small.svg` (the board's dark-symbol colours), copied byte for byte to `wwwroot/favicon.svg` and the site's `assets/favicon.svg`; the site footer uses a fixed-colour copy |
| P4 | Atlas | The site README does not name the brand files that MIT does not cover | ✅ Yes (iteration 2) | List `assets/symbol.svg`, `assets/favicon.svg` and `assets/symbol-small-light.svg` |
| P4 | Atlas | The site uses European Portuguese forms ("sítios", "logótipo", "Secções", "À chegada", "As suas fotos…") | ✅ Yes (iteration 2): English plus pt-BR | Owner's decision: `index.html` in English, `pt-br/index.html` in Brazilian Portuguese, an EN / PT-BR switch with `hreflang`; the owner also had the GitHub link and every open-source mention removed from the site (the web app's UI has none) |

Decision: only P4 rows, fixed together with the owner's language decision → iteration 2.

## Iteration 2

Diff: `fix/brand-alignment` from `4e155f0` to `138b9ec` (platform), `de64148` to `08425cb` (site), `d0aeb1b` to `20d080c` (brand).

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P2 | Atlas | The site's copy presents features that are not built yet (sources, steps, example questions, privacy points) | Open | Waiting on the owner: mark unbuilt features as "coming soon" or keep the full vision |
| P3 | Atlas | The site README still described a single Portuguese page | ✅ Yes (iteration 3) | Describe the English `index.html` and the pt-BR `pt-br/index.html` sharing `styles.css` and `assets/`, and that the Pages workflow publishes `pt-br/` |
| P4 | Atlas | pt-BR wording: "Ligue o HD", "centro de dados" | ✅ Yes (iteration 3) | "Conecte o HD externo", "data center" |
| P4 | Atlas | The site's `symbol-small-light.svg` had no source in `sepiabox-brand` | ✅ Yes (iteration 3) | Added to `sepiabox-brand/assets` byte for byte and listed in its README |
| P4 | Spock | The EN / PT-BR links had no accessible names | ✅ Yes (iteration 3) | `aria-label="English"` and `aria-label="Português (Brasil)"`; the switch test finds the links by those names |

Decision: P2 open, waiting on the owner's decision.
