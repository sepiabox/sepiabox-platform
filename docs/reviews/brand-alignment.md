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

Decision: no findings open → pull requests.
