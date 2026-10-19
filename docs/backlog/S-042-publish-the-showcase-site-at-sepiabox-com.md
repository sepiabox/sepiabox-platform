## S-042 — Publish the showcase site at sepiabox.com
**Epic:** Site
**Depends on:** none
**Priority:** Low
**Size:** S

### Context
`sepiabox.com` presents the project while the app is private. **Repository: `sepiabox-site`.**

### Scope
- Single static page (HTML + CSS, no framework) in Portuguese: header with the symbol (copy `assets/symbol.svg` from `sepiabox-brand`) and "sepiabox"; hero "Pergunte às suas fotos." with an example card ("Quantos casamentos fomos em 2026?" → "4 casamentos este ano"); "Junta o que hoje está espalhado" (HD externo, Google Fotos, Google Drive, OneDrive, iPhone e Android em breve); "Como funciona" (Junte, Entenda, Pergunte); six example questions; privacy band (fotos suas, rostos só se ligar, guardadas na Europa, código aberto); footer with a link to GitHub.
- Colors and typefaces from `docs/architecture.md` in `sepiabox-platform` (visual identity).
- GitHub Pages workflow; `CNAME` file with `sepiabox.com`.

### Out of scope
- Waitlist with a backend.

### Acceptance criteria
- [ ] Given the HTML, then `html-validate` passes in CI.
- [ ] Given the page at 375 px width (Playwright), then there is no horizontal scroll (`scrollWidth <= clientWidth`).
- [ ] Given the page, then the sections `#como`, `#perguntas` and `#privacidade` exist.

### Technical notes
- The domain's DNS is a human step (`docs/operations.md`).
- The site code is MIT (the `LICENSE` already exists); the symbol copied from `sepiabox-brand` is not: the footer says "Nome e logótipo Sepiabox: todos os direitos reservados" (D-010).

### Expected tests
- CI: `html-validate` and a mobile-width Playwright test.
