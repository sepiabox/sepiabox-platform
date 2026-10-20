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
| The deploy identity needs *AcrDelete* (or Contributor) on the container registry, for the image purge after each deploy (Owner on `rg-sepiabox` already includes it) | S-034 | Azure portal → Container Registry → Access control |
| Google OAuth client (Testing mode, the two e-mails as test users; Drive API enabled) | S-010, S-037 | Google Cloud Console |
| Microsoft app registration for personal accounts (sign-in and Files.Read) | S-010, S-039 | Azure portal → Entra ID |
| Anthropic API key for `claude-haiku-5-5` (an Anthropic Console organization with billing and a low monthly spend limit; billed separately, outside the €50/month Azure ceiling of D-008); locally in the web's user secrets as `Anthropic:ApiKey`, in Azure as the Key Vault secret `Anthropic--ApiKey` (D-052) | S-025, S-058 | Anthropic Console; Key Vault |
| DNS records at GoDaddy: `app` (CNAME + TXT `asuid.app`) and `sepiabox.com` for GitHub Pages | S-035, S-042 | GoDaddy |
| GitHub Pages enabled on `sepiabox-site` with source "GitHub Actions", then "Enforce HTTPS" once the certificate is issued; DNS: A records for `@` and a CNAME for `www` (values in the sepiabox-site README) | S-042 | GitHub → sepiabox-site → Settings → Pages; GoDaddy |
| List of allowed e-mails (`Access:Emails`) in Key Vault | S-010, S-058 | Key Vault |
| Sign-in secrets in Key Vault: `Auth--Google--ClientId`, `Auth--Google--ClientSecret`, `Auth--Microsoft--ClientId`, `Auth--Microsoft--ClientSecret`, `Access--Emails` | S-058 | Key Vault |
| Deploy tools on the computer that deploys: the [Azure Developer CLI](https://aka.ms/azd-install) (`azd`), the Azure CLI (`az`) with the `containerapp` extension (`az extension add --name containerapp`), the .NET 10 SDK and Docker, plus Bash (Linux, macOS, WSL or Git Bash). Sign in to both: `azd auth login` and `az login` | S-032 | Your computer |
| The `sepiabox-logs` Log Analytics workspace deployed by `sepiabox-infra` (S-031) before the first deploy: the app's Application Insights and Container Apps environment write to it and publishing never creates another one | S-032 | sepiabox-infra deploy workflow |
| An azd environment named `sepiabox` in France Central, once per computer: `azd env new sepiabox --location francecentral --subscription <subscription id>` in the repository root. azd names the resource group `rg-<environment>`, so the name must be `sepiabox`. The environment lives in `.azure/` (ignored by git) and keeps the generated PostgreSQL login and password; keep that folder, or the next deploy generates new ones | S-032 | Your computer |
| Storage Blob Data Contributor on the storage account for each person who runs `sepiabox-ingest upload --destination azure` (Owner on the resource group grants no data access): give their Entra object ids to S-058's `uploaderPrincipalIds` parameter (`az ad signed-in-user show --query id -o tsv`) | S-058, upload | azd environment; Azure portal |
| After the first deploy: add `https://<Container Apps default host>/signin-google` and `/signin-microsoft` to the Google and Microsoft OAuth clients | S-058 | Google Cloud Console; Azure portal → Entra ID |
| Anthropic API key for the agents' required live golden runs (S-025, S-026, S-054), as an environment variable of the agents' environment: Claude Code environment settings → Edit → environment variables, `ANTHROPIC_API_KEY` = the Anthropic API key (the network-secret form cannot send a header without a prefix, which the API's `x-api-key` needs). The `LiveApi` tests read `ANTHROPIC_API_KEY`, the SDK's default; agents never print or log it. Set a monthly spend limit in the Anthropic Console. It takes effect in new sessions. Never in the repository or CI; tests in `Category=LiveApi` are excluded from CI. Without it those stories are `blocked` (D-054). Production is unchanged: the web reads `Anthropic:ApiKey` from Key Vault (S-058) | S-025, S-026, S-054 | Claude Code environment settings; Anthropic Console |
| Video playback check, against Azurite (`dotnet run --project src/Sepiabox.AppHost`) or Azure after a deploy: in Chrome and in Safari on an iPhone, open the 720p version of one `.MOD` and one `.MTS` clip; play, seek forward and back, pause for 15 minutes and resume (it must carry on, D-049). Then force a failure while a video plays (stop Azurite, or delete that `proxy-720p.mp4` blob): the browser's network panel shows one more request to `photos/<id>/proxy-720p?retry=…`, after which "Couldn't play the video. Download the original." appears under the player (in `pt-BR`: "Não foi possível reproduzir o vídeo. Baixe o original."). Last, a failure that repeats at the same point: replace one `proxy-720p.mp4` with a copy cut to half its size and play past the cut; there is at most one `?retry=` request, then the message, and no further requests. Also, in both browsers, pause a video for longer than its link lasts (over 4 hours, or shorten `VideoLifetime` locally) and resume: it must carry on after one `?retry=` request; and note the error code the browser reports for the expired link (run `document.querySelector("video").error?.code` in the console right after resuming; 2 is a network error, 4 an unusable source). This is the only test of `wwwroot/video.js` | S-011, S-032 | Browser |

## Owner acceptance (manual checks)

Checks only a person can do. A story whose automated criteria pass is `done` and lists its manual checks here (D-054); the owner runs them and reports a failure as an issue.

| Check | Story |
|---|---|
| After `scripts/deploy.sh` (see "Deploying to Azure"): (1) `az containerapp job execution list --name sepiabox-migrate --resource-group rg-sepiabox --query "[0].{status:properties.status,end:properties.endTime}" -o table` shows `Succeeded`, and its end time is before the `createdTime` of the web's newest revision in `az containerapp revision list --name sepiabox-web --resource-group rg-sepiabox --query "[].{name:name,created:properties.createdTime}" -o table`. (2) With `host=$(az containerapp show --name sepiabox-web --resource-group rg-sepiabox --query properties.configuration.ingress.fqdn -o tsv)`, `curl -s -o /dev/null -w "%{http_code}\n" https://$host/health` prints `200` (the first call after scale to zero can take up to a minute). (3) `curl -s -o /dev/null -w "%{http_code}\n" http://$host/health` prints `301` or `308` (the ingress sends plain HTTP to HTTPS). (4) `curl -s -o /dev/null -w "%{http_code}\n" -H "Host: example.com" https://$host/health` does not print `200` (`AllowedHosts`; the ingress itself may answer 404) | S-032 |
| Sign in with Google and with Microsoft on `https://<Container Apps default host>`, restart the web revision, reload (still signed in); then try an account not on the list (lands on the no-access page) | S-058 |
| The same on `https://app.sepiabox.com` once the custom domain is set | S-035 |
| `upload` one folder to Azure: its assets appear in the cloud timeline without running anything else; the dead-letter count stays 0 | S-033 |
| After the GeoNames job (`az containerapp job start --name sepiabox-geonames --resource-group rg-sepiabox`): a photo taken in Lisbon shows "Lisbon" on its detail page in Azure | S-059 |
| The first description search after a cold start answers within 30 s, later ones within 2 s | S-056 |
| MVP acceptance on the real data: after uploading the drive and the import finishing, ask in the web a few counting questions (photos per year, videos per year, items with no date) and compare each answer with `sepiabox-ingest report` on every work folder that was uploaded, in upload order; no test data is ever uploaded to Azure | S-054 |
| The video playback check in the table above | S-011 |
| In Azure, with the browser set to English: the timeline's month headings read like "July 2019"; choose Português (Brasil) in the header: the same page comes back with "julho de 2019", and the detail page's date reads like "14 de julho de 2019, 10:32" (the container image has ICU, D-058) | S-044 |

## Deploying to Azure

Everything the app runs on is described by the AppHost (`src/Sepiabox.AppHost`) and provisioned by azd in `rg-sepiabox`, France Central (S-032). Deploy only with the script, from the repository root:

```bash
azd env select sepiabox
./scripts/deploy.sh
```

It refuses to start unless the selected azd environment is `sepiabox` in `francecentral`, then:

1. `azd provision`: PostgreSQL `sepiabox-pg` (B1ms, 32 GB, auto-grow off, PostgreSQL 17, `vector` and `postgis` allowed), the storage account with its private containers (`originals`, `derivatives`, `manifests`, `incoming`, `exports`, `dataprotection`), Key Vault, the container registry (Basic), Application Insights on `sepiabox-logs`, the Container Apps environment and the two managed identities. The first run asks nothing: azd generates the PostgreSQL login and password and keeps them in `.azure/`.
2. `azd deploy migrate`: builds the worker image and updates the `sepiabox-migrate` Container Apps job (manual trigger, 10 minutes, no retries).
3. Starts one execution of `sepiabox-migrate` (`sepiabox-worker migrate` against the cloud database) and waits for it, up to 15 minutes.
4. Only if it succeeded, `azd deploy web`: builds the web image and creates the `sepiabox-web` revision (0–1 replicas, HTTPS ingress, `AllowedHosts` = its default host).

If the migration fails, is stopped or times out, the script stops before step 4 and the running web is left as it was; the job's logs are in the `sepiabox-logs` workspace (table `ContainerAppConsoleLogs_CL`, container `migrate`). Fix the cause and run the script again: migrations already applied are skipped. `azd up` and a bare `azd deploy` are refused by the `predeploy` hook in `azure.yaml`, since they would start the web before the migrations.

Until S-058, the web has no sign-in secrets in Azure: it starts with no sign-in buttons and lets no one in, but `/health` answers. The web's address is `https://sepiabox-web.<environment default domain>`: `az containerapp show --name sepiabox-web --resource-group rg-sepiabox --query properties.configuration.ingress.fqdn -o tsv`.

## MVP acceptance on the real data

After uploading the drive to Azure (below) and the automatic import finishing, run `sepiabox-ingest report <workdir>...` on every work folder you have uploaded, in the order you uploaded them (an asset in several folders counts once and takes its date from the last one, as the cloud does): it counts unique assets per year and kind, and the undated ones, from its manifests, the same unit as the catalog (an exact duplicate counts once; `scan` counts files instead). Then open the app and ask, in English and in Portuguese: photos per year for three years, videos per year for two years, and how many items have no date. Each answer must equal `report`'s number. A difference is reported as an issue with the question, both numbers and the year. Cities are not part of this check (`report` has no place names). No test data is ever uploaded to Azure for this (S-054, D-056).

## Uploading the drive to Azure

1. Once: be listed in `uploaderPrincipalIds` (see the prerequisites) and run `az login` on the computer with the work folder; `upload` signs in with `DefaultAzureCredential`, so the Azure CLI login is enough.
2. Run `sepiabox-ingest upload <work folder> --destination azure --account <storage account name>` (the name is in the azd outputs).
3. It resumes where it stopped: run the same command again after an interruption. Originals go to the Cold tier and each is read once (D-030); manifests go last, so the cloud imports only complete assets (S-033).
4. A file reported as changed since `process` is not uploaded: run `process` again for it.
5. Never run `upload` while `sepiabox-worker assets delete --yes` runs (S-057, after the MVP): a manifest uploaded during the deletion can lose its blobs.

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

## Importing exports (after the MVP)

Google Photos, Apple Photos / iCloud, Instagram and Facebook are imported from their official exports (D-053, Sprints 19–21), which only the account owner can request:

| Source | How to get it |
|---|---|
| Google Photos | takeout.google.com → only Google Photos → zip parts; extract them all into one folder |
| Apple Photos | On a Mac, Photos → File → Export → Export Unmodified Originals (tick "Export IPTC as XMP"); or privacy.apple.com → Request a copy of your data → iCloud Photos |
| Instagram, Facebook | Accounts Center → Your information and permissions → Download your information → format **JSON**, media quality high |

Keep the extracted export on the PC and run `sepiabox-ingest process <folder> --export <kind>`; the export is only read. Exports hold personal data: never commit them or attach them to issues. When a story asks for a small real export to confirm the format, share it privately with the person running it.

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

Without any OAuth client, in Development, `/sign-in` shows an e-mail field instead (it posts to `/dev-login`): it signs in any e-mail on `Access:Emails` without a password, only from the same computer. `/dev-login` answers 404 in every other environment, and also in Development once a client is configured.

## OAuth redirect URLs

- Local: `https://localhost:7180/signin-google`, `/signin-microsoft` (the `https` port in `src/Sepiabox.Web/Properties/launchSettings.json`)
- Azure, before the custom domain: `https://<Container Apps default host>/signin-google`, `/signin-microsoft` (S-032)
- Production: `https://app.sepiabox.com/signin-google`, `/signin-microsoft` (S-035)

## Emergency shutdown

See `sepiabox-infra/scripts/emergency-stop.sh` (turns off web traffic, stops PostgreSQL and disables the queue; keeps the data).
