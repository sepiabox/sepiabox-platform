# Operations

Steps that need a person, because they depend on accounts, consents or external registrations. Stories that depend on these steps say so in their technical notes; the code and automated tests of those stories do not depend on them.

## Human prerequisites (one time)

| Step | Needed for | Where |
|---|---|---|
| Azure subscription billed in EUR, with Owner access to the `rg-sepiabox` resource group | S-029 onwards (deploy) | Azure portal |
| Entra ID app registration with a federated credential (OIDC) for `sepiabox/sepiabox-platform` and `sepiabox/sepiabox-infra`, environment `production` | S-029, S-034 | Azure portal → Entra ID |
| GitHub variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` and a `production` environment with manual approval | S-029, S-034 | GitHub → Settings |
| Google OAuth client (Testing mode, the two e-mails as test users; Drive API enabled) | S-010, S-037 | Google Cloud Console |
| Microsoft app registration for personal accounts (sign-in and Files.Read) | S-010, S-039 | Azure portal → Entra ID |
| `claude-haiku-5-5` deployment on Microsoft Foundry and its key in Key Vault | S-025 | Microsoft Foundry |
| DNS records at GoDaddy: `app` (CNAME + TXT `asuid.app`) and `sepiabox.com` for GitHub Pages | S-035, S-042 | GoDaddy |
| List of allowed e-mails (`Access:Emails`) in Key Vault | S-010, S-032 | Key Vault |

## OAuth redirect URLs

- Local: `https://localhost:<port>/signin-google`, `/signin-microsoft`
- Production: `https://app.sepiabox.com/signin-google`, `/signin-microsoft`

## Emergency shutdown

See `sepiabox-infra/scripts/emergency-stop.sh` (turns off web traffic, stops PostgreSQL and disables the queue; keeps the data).
