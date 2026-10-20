## S-044 — Localize the web app: English first, Brazilian Portuguese
**Epic:** Web app
**Depends on:** S-011
**Priority:** High
**Size:** M

### Context
The owner decided that the app UI is English by default and localizable, with Brazilian Portuguese (`pt-BR`) as the first translation (D-050, amending D-001). S-009–S-011 wrote the UI in Portuguese, partly with European Portuguese wording ("Descarregar o original"), with Portuguese routes (`/entrar`, `/sem-acesso`, `/sair`, `/fotos/{id}`) and hand-written Portuguese month names (`PortugueseDates`, because the build uses invariant globalization). Every later web story adds UI text, so this story sets the pattern before them.

### Scope
- Request localization in `Sepiabox.Web`: supported cultures `en` (default) and `pt-BR`; the culture cookie wins, then `Accept-Language`, where any `pt` variant (`pt`, `pt-PT`, `pt-BR`) maps to `pt-BR` and anything else to `en` (D-050).
- A language switch in the layout (English / Português (Brasil)) that sets the culture cookie and returns to the same page; works on the sign-in and no-access pages too.
- Every existing user-facing string moves to `.resx` resources with English and `pt-BR` values, read through `IStringLocalizer`: timeline (headings, month groups, "no date" group, empty state), photo detail (labels, download link), sign-in page (buttons per provider, the failed sign-in message, the Development e-mail form), no-access page, sign-out button, not-found and error pages, the reconnect modal, page titles, `alt`/`title`/`aria-label` text, the video player (S-011: "still processing" message and any messages of `video.js`, passed to the script through `data-` attributes, never written in the script).
- Date formatting per culture: month-and-year headings and the detail page's date and time come from the current culture ("July 2019" / "julho de 2019"; "July 14, 2019, 10:32" / "14 de julho de 2019, 10:32"); `PortugueseDates` is removed.
- `<html lang>` follows the culture (`en` or `pt-BR`).
- Routes renamed to English (D-050): `/entrar` → `/sign-in` (and `/sign-in/{provider}`), `/sem-acesso` → `/no-access`, `/sair` → `/sign-out`, `/fotos/{id}` → `/photos/{id}`, `/fotos/{id}/{derivative}` and `/fotos/{id}/original` → `/photos/{id}/...`; query parameter `erro` → `error`. The old routes are not redirected (the app was never published). `AuthSetup`'s path constants, the anonymous-route list, the return-URL loop check and the links in pages change with them.
- Update `docs/operations.md` (sign-in section and the S-032 smoke-test step: `/no-access`, `/sign-in`) and the route names written in D-039 and D-048 (one line in `docs/decisions.md` saying they were renamed by S-044, D-050).

### Out of scope
- Other languages (adding one is a new `.resx` file and a supported culture).
- Translating data the family typed (people, places, albums) or GeoNames place names.
- Storing a per-user language preference in the catalog (the cookie is enough for two users).

### Acceptance criteria
- [ ] Given a request with `Accept-Language: en-US` and no culture cookie, when the timeline renders, then its text is English and `<html lang="en">`.
- [ ] Given `Accept-Language: pt-PT` (or `pt-BR`, or `pt`), when the timeline renders, then its text is Brazilian Portuguese and `<html lang="pt-BR">`.
- [ ] Given `Accept-Language: de-DE`, when a page renders, then it is English.
- [ ] Given a culture cookie for `en` and `Accept-Language: pt-BR`, when a page renders, then it is English (the explicit choice wins).
- [ ] Given the language switch, when I choose Português (Brasil) on the photo detail, then I return to the same photo with the text in `pt-BR`.
- [ ] Given each culture, when the timeline groups July 2019, then the heading is "July 2019" in English and "julho de 2019" in `pt-BR`; and the detail page shows the date and time in that culture's format.
- [ ] Given the `pt-BR` resources, when they are checked, then every key of the English resources has a non-empty `pt-BR` value and none uses the pt-PT words listed in D-050 (a test over the `.resx` files).
- [ ] Given the Razor components, scripts and C# of `Sepiabox.Web`, when they are checked, then the user-facing strings of the pages listed in Scope come from the localizer (bUnit tests render each page in both cultures and assert the expected text).
- [ ] Given a video without a proxy, when its detail page renders in each culture, then the processing message is in that culture; and the `video.js` messages come from `data-` attributes rendered in that culture.
- [ ] Given an anonymous request to `/`, then it is redirected to `/sign-in?ReturnUrl=%2F`; `/photos/{id}/thumb-256` behaves as `/fotos/...` did (D-039, D-048); `/entrar`, `/sem-acesso` and `/fotos/{id}` answer 404 (or, signed out, the sign-in redirect that any unknown address gets, D-048).
- [ ] Given someone signed in but not on the access list, when they open a page, then they are redirected to `/no-access`, which is localized.

### Technical notes
- The build sets `InvariantGlobalization` in `Directory.Build.props`, where `new CultureInfo("pt-BR")` throws and culture formatting is invariant. Turn it off for `Sepiabox.Web` only (the other projects keep it) and check that the web's container base image includes ICU (the default `aspnet` Debian/Ubuntu images do; chiseled or Alpine images need the ICU variant); a test that `CultureInfo.GetCultureInfo("pt-BR").DateTimeFormat.MonthNames[6]` is "julho" guards it. If ICU is unavailable, keep explicit month names per culture in the resources instead, and record the choice.
- `AddLocalization` + `UseRequestLocalization` with a small custom `RequestCultureProvider` for the `pt*` mapping after the cookie provider; the switch posts to a minimal endpoint (antiforgery token) that writes `CookieRequestCultureProvider.MakeCookieValue` and redirects to a local return URL (same validation as the sign-in return URL). It is anonymous, like the sign-in routes (D-048).
- In interactive server components the circuit keeps the culture of the request that started it; the switch reloads the page (forced navigation), so no circuit-level culture change is needed.
- One `.resx` per component folder or a shared `Resources/SharedResource.resx`, whichever keeps keys simplest; English text as the neutral resource, `pt-BR` as the satellite.
- Keep the existing tests' intent; they assert English text by default and add `pt-BR` cases where a page has text.
- Decisions: D-039, D-048, D-050.

### Expected tests
- Unit: culture selection (cookie, `pt*` mapping, fallback), resource completeness and the pt-PT word check, date formatting per culture.
- bUnit: timeline, photo detail, video player, sign-in and no-access pages in `en` and `pt-BR`.
- Integration (`WebApplicationFactory`): `Accept-Language` and cookie end to end, the language switch, the renamed routes and the redirects of D-048 under the new paths.
