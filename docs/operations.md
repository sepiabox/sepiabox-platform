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
- Manifests written by `process --faces` carry faces (S-018). Update every computer that runs `sepiabox-ingest` before uploading them: an older `upload` accepts them but does not know how to merge faces, so uploading a manifest without faces from an old build could replace the stored faces. To add them back, keep that work folder, run `process --faces` on it again (faces come from the previews, without the drive) and `upload` from it with the updated build.
- Files processed before S-043 keep the date they had (none, for undated videos): `process` skips unchanged files. To date them from their folder or file name, run `process` again into a fresh work folder and upload from it.

## OAuth redirect URLs

- Local: `https://localhost:<port>/signin-google`, `/signin-microsoft`
- Production: `https://app.sepiabox.com/signin-google`, `/signin-microsoft`

## Emergency shutdown

See `sepiabox-infra/scripts/emergency-stop.sh` (turns off web traffic, stops PostgreSQL and disables the queue; keeps the data).
