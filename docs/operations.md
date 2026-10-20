# Operations

Steps that need a person, because they depend on accounts, consents or external registrations. Stories that depend on these steps say so in their technical notes; the code and automated tests of those stories do not depend on them.

## Human prerequisites (one time)

| Step | Needed for | Where |
|---|---|---|
| Azure subscription billed in EUR, with Owner access to the `rg-sepiabox` resource group | S-029 onwards (deploy) | Azure portal |
| Entra ID app registration with a federated credential (OIDC) for `sepiabox/sepiabox-platform` and `sepiabox/sepiabox-infra`, environment `production` | S-029, S-034 | Azure portal → Entra ID |
| GitHub variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` and a `production` environment with manual approval | S-029, S-034 | GitHub → Settings |
| GitHub variable `BUDGET_ALERT_EMAILS` in `sepiabox-infra`: comma-separated e-mails that receive the budget and cost-anomaly alerts | S-029 | GitHub → sepiabox-infra → Settings → Variables |
| The deploy identity (the OIDC app registration) needs Owner, or User Access Administrator plus Resource Policy Contributor, on `rg-sepiabox`, to assign the shutdown runbook's role and the resource policy | S-030, S-031 | Azure portal → rg-sepiabox → Access control |
| Google OAuth client (Testing mode, the two e-mails as test users; Drive API enabled) | S-010, S-037 | Google Cloud Console |
| Microsoft app registration for personal accounts (sign-in and Files.Read) | S-010, S-039 | Azure portal → Entra ID |
| `claude-haiku-5-5` deployment on Microsoft Foundry and its key in Key Vault | S-025 | Microsoft Foundry |
| DNS records at GoDaddy: `app` (CNAME + TXT `asuid.app`) and `sepiabox.com` for GitHub Pages | S-035, S-042 | GoDaddy |
| GitHub Pages enabled on `sepiabox-site` with source "GitHub Actions", then "Enforce HTTPS" once the certificate is issued; DNS: A records for `@` and a CNAME for `www` (values in the sepiabox-site README) | S-042 | GitHub → sepiabox-site → Settings → Pages; GoDaddy |
| List of allowed e-mails (`Access:Emails`) in Key Vault | S-010, S-032 | Key Vault |
| Sign-in secrets in Key Vault: `Auth--Google--ClientId`, `Auth--Google--ClientSecret`, `Auth--Microsoft--ClientId`, `Auth--Microsoft--ClientSecret`, `Access--Emails` | S-032 | Key Vault |
| After the first deploy: sign in with Google and with Microsoft on `https://app.sepiabox.com`, restart the web revision, reload (still signed in); then try an account not on the list (lands on `/sem-acesso`) | S-032 | Browser; Azure portal → Container Apps → Revisions |
| Video playback check, against Azurite (`dotnet run --project src/Sepiabox.AppHost`) or Azure after a deploy: in Chrome and in Safari on an iPhone, open the 720p version of one `.MOD` and one `.MTS` clip; play, seek forward and back, pause for 15 minutes and resume (it must carry on, D-049). Then force a failure while a video plays (stop Azurite, or delete that `proxy-720p.mp4` blob): the browser's network panel shows one more request to `fotos/<id>/proxy-720p?retry=…`, after which "Não foi possível reproduzir o vídeo. Descarregue o original." appears under the player. Last, a failure that repeats at the same point: replace one `proxy-720p.mp4` with a copy cut to half its size and play past the cut; there is at most one `?retry=` request, then the message, and no further requests. Also, in both browsers, pause a video for longer than its link lasts (over 4 hours, or shorten `VideoLifetime` locally) and resume: it must carry on after one `?retry=` request; and note the error code the browser reports for the expired link (run `document.querySelector("video").error?.code` in the console right after resuming; 2 is a network error, 4 an unusable source). This is the only test of `wwwroot/video.js` | S-011, S-032 | Browser |

## Ingesting a drive

- Run `sepiabox-ingest models download` once per computer before `process`, so embeddings (and faces, with `--faces`) are computed in the same pass.
- Faces are opt-in (biometric data): only `process --faces` looks for them (S-018, D-047). Decide before the first full run of the drive; a later run with `--faces` adds them from the work folder without the drive, as long as the work folder is kept.
- Before the first full run, check one of your own Canon `.CR2` files. The tests only use a synthetic CR2 (D-045).
  1. Copy the file into a new, empty temporary folder.
  2. Run `process` on that folder with a separate, new `--out` folder.
  3. Open `derivatives/<id>/preview-1280.webp` in that `--out` folder. It should look like the camera's JPEG: right colours, upright, not a 160x120 thumbnail blown up.
  4. Delete both folders. Never `upload` from that `--out` folder: its manifest names the temporary copy as a source.
- `process` lists each file it leaves out ("Left out <path>: <reason>") next to its problems: a tracker-music module, or a file with no video stream or only cover art (D-045). Left-out files are never catalogued. If one is a real video, report it. Later runs only count the files left out earlier; `--list-left-out` lists them, and `--retry-left-out` reads them again (for example after installing a different FFmpeg). Never `touch` or re-save the originals to force a retry: their modification time is the only capture date some camcorder files have.
- Keep the `process` work folder (`manifests/` and `derivatives/`) until every file has its embedding, and its faces if you use `--faces`: a later `process` run adds them from the previews in the work folder without the drive, but cannot once they are deleted. The summary has no "have no embedding" or "no preview or poster" warning when nothing is left to add.
- Manifests written by `process` from S-043 on may carry `capturedAtSource` (D-046). Older `sepiabox-ingest` and `sepiabox-worker` builds reject that field, so update every computer that runs `sepiabox-ingest` and deploy the worker before uploading such manifests; otherwise `upload` from an old build fails to merge them and the old worker's import reports them as problems.
- Manifests written by `process --faces` carry faces and `facesVersion` (S-018, D-047). Older `sepiabox-ingest` and `sepiabox-worker` builds reject `facesVersion`, so update every computer that runs `sepiabox-ingest` and deploy the worker before uploading manifests from a `--faces` run; otherwise `upload` from an old build fails to merge them (that asset is reported and retried, the stored blob is left alone) and the old worker's import reports them as problems.
- Files processed before S-043 keep the date they had (none, for undated videos): `process` skips unchanged files. To date them from their folder or file name, run `process` again into a fresh work folder and upload from it.

## Sign-in configuration

The web reads these keys (D-048). Locally they go into the web's user secrets; in Azure into Key Vault, where `:` becomes `--` in the secret name (`Auth--Google--ClientId`, `Access--Emails`). Never put them in `appsettings*.json`.

| Key | Value |
|---|---|
| `Auth:Google:ClientId`, `Auth:Google:ClientSecret` | The Google OAuth client (web application) |
| `Auth:Microsoft:ClientId`, `Auth:Microsoft:ClientSecret` | The Microsoft app registration, for personal Microsoft accounts only, with a client secret |
| `Access:Emails` | The allowed e-mails, separated by commas; casing does not matter. Empty or missing lets no one in. List the address each provider reports for the account: the primary address shown on the Google account page (myaccount.google.com) or the Microsoft account page (account.microsoft.com), not another alias |

A provider without both of its keys is left out (its button is not shown); one key without the other stops the web from starting. A change to `Access:Emails` takes effect when the configuration reloads: locally when the user secrets file changes, in Azure with the next restart or revision (Container Apps secrets do not reload). Someone removed from the list loses access on their next request, and an open page within 5 minutes. Locally:

```bash
dotnet user-secrets set "Access:Emails" "<e-mail>,<e-mail>" --project src/Sepiabox.Web
dotnet user-secrets set "Auth:Google:ClientId" "<client id>" --project src/Sepiabox.Web
dotnet user-secrets set "Auth:Google:ClientSecret" "<client secret>" --project src/Sepiabox.Web
```

Without any OAuth client, in Development, `/entrar` shows an e-mail field instead (it posts to `/dev-login`): it signs in any e-mail on `Access:Emails` without a password, only from the same computer. `/dev-login` answers 404 in every other environment, and also in Development once a client is configured.

## OAuth redirect URLs

- Local: `https://localhost:7180/signin-google`, `/signin-microsoft` (the `https` port in `src/Sepiabox.Web/Properties/launchSettings.json`)
- Production: `https://app.sepiabox.com/signin-google`, `/signin-microsoft`

## Emergency shutdown

See `sepiabox-infra/scripts/emergency-stop.sh` (turns off web traffic, stops PostgreSQL and disables the queue; keeps the data).
