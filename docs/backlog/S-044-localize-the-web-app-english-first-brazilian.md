## S-044 — Localize the web app: English first, Brazilian Portuguese
**Epic:** Web app
**Depends on:** S-011
**Priority:** High
**Size:** M

### Context
The owner decided that the app UI is English by default and localizable, with Brazilian Portuguese (`pt-BR`) as the first translation (D-050, amending D-001). S-009–S-011 wrote the UI in Portuguese, partly with European Portuguese wording ("Descarregar o original", "Descarregue o original"), and with hand-written Portuguese month names (`PortugueseDates`, because the build uses invariant globalization). Every later web story adds UI text, so this story sets the pattern before them. Renaming the Portuguese routes to English is S-055.

### Scope
- Request localization in `Sepiabox.Web`: supported cultures `en` (default) and `pt-BR`; the culture cookie wins, then `Accept-Language`, where any `pt` variant (`pt`, `pt-PT`, `pt-BR`) maps to `pt-BR` and anything else to `en` (D-050).
- A language switch in the layout (English / Português (Brasil)) that sets the culture cookie and returns to the same page; works on the sign-in and no-access pages too.
- Every existing user-facing string moves to `.resx` resources with English and `pt-BR` values, read through `IStringLocalizer`: timeline (headings, month groups, "no date" group, empty state), photo detail (labels, download link), sign-in page (buttons per provider, the failed sign-in message, the Development e-mail form), no-access page, sign-out button, not-found and error pages, the reconnect modal, page titles, `alt`/`title`/`aria-label` text, and S-011's components, now on main: `VideoPlayer.razor` ("Vídeo ainda em processamento", the player's `aria-label`, and the playback error that `video.js` reads from the `data-message` attribute, today "Não foi possível reproduzir o vídeo. Descarregue o original.") and `MediaCard.razor` (play label and duration text). Messages stay out of `video.js` itself (D-049).
- Date formatting per culture: month-and-year headings and the detail page's date and time come from the current culture ("July 2019" / "julho de 2019"; "July 14, 2019, 10:32" / "14 de julho de 2019, 10:32"); `PortugueseDates` is removed.
- `<html lang>` follows the culture (`en` or `pt-BR`).
- `docs/operations.md`: the video playback check quotes the error message; replace the pt-PT text with the English text and its `pt-BR` translation ("Couldn't play the video. Download the original." / "Não foi possível reproduzir o vídeo. Baixe o original.").

### Out of scope
- Renaming routes to English (S-055).
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
- [ ] Given the `pt-BR` resources, when they are checked, then every key of the English resources has a non-empty `pt-BR` value and none uses the pt-PT words listed in D-050 or "descarregar" in any form (a test over the `.resx` files).
- [ ] Given the pages listed in Scope, when bUnit renders each in both cultures, then the expected text appears and no Portuguese literal remains in the Razor components, scripts or C# of `Sepiabox.Web` (a source check for the known literals).
- [ ] Given a video without a proxy, when its detail page renders in each culture, then the processing message is in that culture; and the player's `data-message` is in that culture.

### Technical notes
- The build sets `InvariantGlobalization` in `Directory.Build.props`, where `new CultureInfo("pt-BR")` throws and culture formatting is invariant. Turn it off for `Sepiabox.Web` only (the other projects keep it) and check that the web's container base image includes ICU (the default `aspnet` Debian/Ubuntu images do; chiseled or Alpine images need the ICU variant); a test that `CultureInfo.GetCultureInfo("pt-BR").DateTimeFormat.MonthNames[6]` is "julho" guards it. If ICU is unavailable, keep explicit month names per culture in the resources instead, and record the choice.
- `AddLocalization` + `UseRequestLocalization` with a small custom `RequestCultureProvider` for the `pt*` mapping after the cookie provider; the switch posts to a minimal endpoint (antiforgery token) that writes `CookieRequestCultureProvider.MakeCookieValue` and redirects to a local return URL (same validation as the sign-in return URL). It is anonymous, like the sign-in routes (D-048).
- In interactive server components the circuit keeps the culture of the request that started it; the switch reloads the page (forced navigation), so no circuit-level culture change is needed.
- One `.resx` per component folder or a shared `Resources/SharedResource.resx`, whichever keeps keys simplest; English text as the neutral resource, `pt-BR` as the satellite.
- Keep the existing tests' intent; they assert English text by default and add `pt-BR` cases where a page has text.
- Decisions: D-048, D-049, D-050.

### Expected tests
- Unit: culture selection (cookie, `pt*` mapping, fallback), resource completeness and the pt-PT word check, date formatting per culture.
- bUnit: timeline, photo detail, video player, media card, sign-in and no-access pages in `en` and `pt-BR`.
- Integration (`WebApplicationFactory`): `Accept-Language` and cookie end to end, the language switch.
