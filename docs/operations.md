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

- Run `sepiabox-ingest models download` once per computer before `process`, so embeddings are computed in the same pass.
- Before the first full run, copy one of your own Canon `.CR2` files into an empty folder, run `process` on that folder, and open `derivatives/<id>/preview-1280.webp` in the work folder: it should look like the camera's JPEG (right colours, upright, not a 160x120 thumbnail blown up). The tests only use a synthetic CR2 (D-045).
- Keep the `process` work folder (`manifests/` and `derivatives/`) until every file has its embedding, and its faces once S-018 is done: a later `process` run adds them from the previews in the work folder without the drive, but cannot once they are deleted. The summary has no "have no embedding" or "no preview or poster" warning when nothing is left to add.

## OAuth redirect URLs

- Local: `https://localhost:<port>/signin-google`, `/signin-microsoft`
- Production: `https://app.sepiabox.com/signin-google`, `/signin-microsoft`

## Emergency shutdown

See `sepiabox-infra/scripts/emergency-stop.sh` (turns off web traffic, stops PostgreSQL and disables the queue; keeps the data).
