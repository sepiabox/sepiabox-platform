# Operations

Steps that need a person, because they depend on accounts, consents or external registrations. Stories that depend on these steps say so in their technical notes; the code and automated tests of those stories do not depend on them.

## Human prerequisites (one time)

| Step | Needed for | Where |
|---|---|---|
| Azure subscription billed in EUR, with Owner access to the `rg-sepiabox` resource group | S-029 onwards (deploy) | Azure portal |
| Resource providers registered on the subscription, once, by someone with rights on the subscription (the deploy identity only has rights on `rg-sepiabox`, and a deploy that needs an unregistered provider fails, as the first sepiabox-infra deploy did on `Microsoft.Automation`): Azure portal → Subscriptions → your subscription → Settings → Resource providers → select each and Register; or `az provider register --namespace <name>` for each of `Microsoft.Automation`, `Microsoft.OperationalInsights`, `Microsoft.Insights`, `Microsoft.AlertsManagement`, `Microsoft.CostManagement`, `Microsoft.App`, `Microsoft.DBforPostgreSQL`, `Microsoft.Storage`, `Microsoft.KeyVault`, `Microsoft.ContainerRegistry`, `Microsoft.ManagedIdentity`, `Microsoft.ServiceBus`, `Microsoft.EventGrid`. Check with `az provider show --namespace <name> --query registrationState -o tsv` (`Registered`) | S-029 onwards (deploy), S-030, S-031, S-032, S-033 | Azure portal → Subscriptions |
| Entra ID app registration with a federated credential (OIDC) for `sepiabox/sepiabox-platform` and `sepiabox/sepiabox-infra`, environment `production`. GitHub sends the subject in its immutable-id format, with the organization's and the repository's numeric ids: issuer `https://token.actions.githubusercontent.com`, audience `api://AzureADTokenExchange`, subject `repo:sepiabox@344892533/sepiabox-platform@1425873797:environment:production` for this repository (the ids come from `gh api orgs/sepiabox --jq .id` and `gh api repos/sepiabox/sepiabox-platform --jq .id`; sepiabox-infra has its own repository id). Keeping a second credential with the name-only subject `repo:sepiabox/sepiabox-platform:environment:production` does no harm. Both formats exist already | S-029, S-034 | Azure portal → Entra ID → App registrations → Certificates & secrets → Federated credentials |
| GitHub variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` and a `production` environment with manual approval | S-029, S-034 | GitHub → Settings |
| GitHub variable `BUDGET_ALERT_EMAILS` in `sepiabox-infra`: comma-separated e-mails that receive the budget and cost-anomaly alerts | S-029 | GitHub → sepiabox-infra → Settings → Variables |
| The deploy identity (the OIDC app registration) needs Owner, or User Access Administrator plus Resource Policy Contributor, on `rg-sepiabox`, to assign the shutdown runbook's role and the resource policy | S-030, S-031 | Azure portal → rg-sepiabox → Access control |
| The deploy identity needs *Contributor* on the container registry (or Owner on `rg-sepiabox`, which includes it) for the image purge after each deploy: `az acr run` starts a registry task, which *AcrDelete* alone does not allow | S-034 | Azure portal → Container Registry → Access control |
| The `production` environment deploys only `main`: GitHub → sepiabox-platform → Settings → Environments → production → Deployment branches and tags → Selected branches and tags → add `main`. The workflow also refuses a manual run of another branch, which would use that branch's copy of the workflow | S-034 | GitHub → Settings → Environments |
| The deploy identity, and anyone who runs `scripts/deploy.sh`, needs rights at **subscription** scope to create deployments and to read and write the resource group: azd deploys the AppHost's infrastructure as a subscription-scoped deployment that declares `rg-sepiabox` itself, which Owner on the resource group alone does not allow. The narrowest choice is a custom role assigned on the subscription with the actions `Microsoft.Resources/deployments/*`, `Microsoft.Resources/subscriptions/read`, `Microsoft.Resources/subscriptions/locations/read`, `Microsoft.Resources/subscriptions/resourceGroups/read` and `Microsoft.Resources/subscriptions/resourceGroups/write`; if azd then reports `AuthorizationFailed` on another read, add that action too. Contributor on the subscription also works but grants far more. If the first deploy from GitHub fails with `AuthorizationFailed` on `Microsoft.Resources/deployments/write` at the subscription, this is the missing step | S-034 | Azure portal → Subscriptions → Access control |
| GitHub secrets of the `production` environment `PG_USERNAME`, `PG_PASSWORD` and `PG_APP_PASSWORD`, and optionally the variable `UPLOADER_PRINCIPAL_IDS`, copied from the first deploy (see "Deploying from GitHub Actions") | S-034 | GitHub → sepiabox-platform → Settings → Environments → production |
| Google OAuth client (Testing mode, the two e-mails as test users; Drive API enabled) | S-010, S-037 | Google Cloud Console |
| Microsoft app registration for personal accounts (sign-in and Files.Read) | S-010, S-039 | Azure portal → Entra ID |
| Anthropic API key for `claude-haiku-5-5` (an Anthropic Console organization with billing and a low monthly spend limit; billed separately, outside the €50/month Azure ceiling of D-008); locally in the web's user secrets as `Anthropic:ApiKey`, in Azure as the Key Vault secret `Anthropic--ApiKey` (D-052) | S-025, S-058 | Anthropic Console; Key Vault |
| DNS records at GoDaddy: for `app.sepiabox.com`, a CNAME `app` → the web's Container Apps host and a TXT `asuid.app` → the environment's custom domain verification id (exact values and order in "Custom domain"); and `sepiabox.com` for GitHub Pages | S-035, S-042 | GoDaddy |
| GitHub variables `CUSTOM_DOMAIN` (`app.sepiabox.com`) and `CUSTOM_DOMAIN_BINDING` (`Disabled`, then `SniEnabled`) in the `production` environment, set in the order in "Custom domain"; unset means no custom domain | S-035 | GitHub → Settings → Environments → production |
| GitHub Pages enabled on `sepiabox-site` with source "GitHub Actions", then "Enforce HTTPS" once the certificate is issued; DNS: A records for `@` and a CNAME for `www` (values in the sepiabox-site README) | S-042 | GitHub → sepiabox-site → Settings → Pages; GoDaddy |
| List of allowed e-mails (`Access:Emails`) in Key Vault | S-010, S-058 | Key Vault |
| Sign-in secrets in Key Vault: `Auth--Google--ClientId`, `Auth--Google--ClientSecret`, `Auth--Microsoft--ClientId`, `Auth--Microsoft--ClientSecret`, `Access--Emails` | S-058 | Key Vault |
| Deploy tools on the computer that deploys: the [Azure Developer CLI](https://aka.ms/azd-install) (`azd`), the Azure CLI (`az`) with the `containerapp` extension (`az extension add --name containerapp`), the .NET 10 SDK and Docker, plus Bash (Linux, macOS, WSL or Git Bash). Sign in to both: `azd auth login` and `az login` | S-032 | Your computer |
| The `sepiabox-logs` Log Analytics workspace deployed by `sepiabox-infra` (S-031) before the first deploy: the app's Application Insights and Container Apps environment write to it and publishing never creates another one | S-032 | sepiabox-infra deploy workflow |
| An azd environment named `sepiabox` in France Central, once per computer: `azd env new sepiabox --location francecentral --subscription <subscription id>` in the repository root. azd names the resource group `rg-<environment>`, so the name must be `sepiabox`. The environment lives in `.azure/` (ignored by git) and keeps the PostgreSQL administrator login and password azd generates on the first provision: back them up (see "Deploying to Azure") | S-032 | Your computer |
| Storage Blob Data Contributor on the storage account for each person who runs `sepiabox-ingest upload --destination azure` (Owner on the resource group grants no data access): give their Entra object ids to the `uploaderPrincipalIds` parameter, comma-separated: each person runs `az ad signed-in-user show --query id -o tsv`, then `azd env config set infra.parameters.uploaderPrincipalIds "<id>,<id>"` (check the key against `.azure/sepiabox/config.json`) and `./scripts/deploy.sh`. Empty by default, so nobody can upload until it is set | S-058, upload | azd environment |
| After the first deploy: add `https://<Container Apps default host>/signin-google` and `/signin-microsoft` to the Google and Microsoft OAuth clients | S-058 | Google Cloud Console; Azure portal → Entra ID |
| Anthropic API key for the agents' required live golden runs (S-025, S-026, S-054), as an environment variable of the agents' environment: Claude Code environment settings → Edit → environment variables, `ANTHROPIC_API_KEY` = the Anthropic API key (the network-secret form cannot send a header without a prefix, which the API's `x-api-key` needs). The `LiveApi` tests read `ANTHROPIC_API_KEY`, the SDK's default; agents never print or log it. Set a monthly spend limit in the Anthropic Console. It takes effect in new sessions. Never in the repository or CI; tests in `Category=LiveApi` are excluded from CI. Without it those stories are `blocked` (D-054). Production is unchanged: the web reads `Anthropic:ApiKey` from Key Vault (S-058) | S-025, S-026, S-054 | Claude Code environment settings; Anthropic Console |
| Video playback check, against Azurite (`dotnet run --project src/Sepiabox.AppHost`) or Azure after a deploy: in Chrome and in Safari on an iPhone, open the 720p version of one `.MOD` and one `.MTS` clip; play, seek forward and back, pause for 15 minutes and resume (it must carry on, D-049). Then force a failure while a video plays (stop Azurite, or delete that `proxy-720p.mp4` blob): the browser's network panel shows one more request to `photos/<id>/proxy-720p?retry=…`, after which "Couldn't play the video. Download the original." appears under the player (in `pt-BR`: "Não foi possível reproduzir o vídeo. Baixe o original."). Last, a failure that repeats at the same point: replace one `proxy-720p.mp4` with a copy cut to half its size and play past the cut; there is at most one `?retry=` request, then the message, and no further requests. Also, in both browsers, pause a video for longer than its link lasts (over 4 hours, or shorten `VideoLifetime` locally) and resume: it must carry on after one `?retry=` request; and note the error code the browser reports for the expired link (run `document.querySelector("video").error?.code` in the console right after resuming; 2 is a network error, 4 an unusable source). This is the only test of `wwwroot/video.js` | S-011, S-032 | Browser |

## Owner acceptance (manual checks)

Checks only a person can do. A story whose automated criteria pass is `done` and lists its manual checks here (D-054); the owner runs them and reports a failure as an issue.

| Check | Story |
|---|---|
| After `scripts/deploy.sh` (see "Deploying to Azure"): (1) `az containerapp job execution list --name sepiabox-migrate --resource-group rg-sepiabox --query "[0].{status:properties.status,end:properties.endTime}" -o table` shows `Succeeded`, and its end time is before the `createdTime` of the web's newest revision in `az containerapp revision list --name sepiabox-web --resource-group rg-sepiabox --query "[].{name:name,created:properties.createdTime}" -o table`. (2) With `host=$(az containerapp show --name sepiabox-web --resource-group rg-sepiabox --query properties.configuration.ingress.fqdn -o tsv)`, `curl -s -o /dev/null -w "%{http_code}\n" https://$host/health` prints `200` (the first call after scale to zero can take up to a minute). (3) `curl -s -o /dev/null -w "%{http_code}\n" http://$host/health` prints `301` or `308` (the ingress sends plain HTTP to HTTPS). (4) `curl -s -o /dev/null -w "%{http_code}\n" -H "Host: example.com" https://$host/health` does not print `200` (`AllowedHosts`; the ingress itself may answer 404) | S-032 |
| After the first deploy has settled (the web may restart a few times while its Key Vault role propagates, see "Secrets in Azure"), the owner's secrets are in Key Vault and the default host's `/signin-google` and `/signin-microsoft` are on the OAuth clients: sign in with Google and with Microsoft on `https://<Container Apps default host>`; restart the web (`az containerapp revision restart --name sepiabox-web --resource-group rg-sepiabox --revision $(az containerapp show --name sepiabox-web --resource-group rg-sepiabox --query properties.latestRevisionName -o tsv)`) and reload: still signed in; deploy again (a new revision) and reload: still signed in; then try an account not on the list: it lands on the no-access page. Also check the key ring exists: the `dataprotection` container has a `keys.xml` blob (Azure portal → storage account → Containers) | S-058 |
| After "Custom domain" is done: `curl -sI https://app.sepiabox.com/health` answers 200 with `strict-transport-security: max-age=31536000`; sign in with Google and with Microsoft on `https://app.sepiabox.com` (after adding its `/signin-google` and `/signin-microsoft` redirect URIs to the OAuth clients): sign-in works; an account not on `Access:Emails` lands on the no-access page | S-035 |
| After the secrets in "Deploying from GitHub Actions" are set: run Actions → Deploy → Run workflow on `main`, approve it in the `production` environment, and check that it succeeds, that its "Deploy" step shows "Migrations applied." before the web is deployed, and that "Purge old images" ran one `acr purge` per repository. Then merge a small change to `main`: the workflow starts by itself once CI is green, and waits for approval | S-034 |
| After a deploy and `sepiabox-import-all` (see "Automatic import"): `upload` one small folder to Azure; within about 30 seconds a new `sepiabox-import` execution starts (see "Check after the first deploy that the job starts"), and within a few minutes, without running anything else, `az containerapp job execution list --name sepiabox-import --resource-group rg-sepiabox --query "[0].properties.status" -o tsv` shows `Succeeded`, its assets appear in the timeline at the Container Apps default host, and the dead-letter count (see "Automatic import") is `0` | S-033 |
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

1. `azd provision`: PostgreSQL `sepiabox-pg` (B1ms, 32 GB, auto-grow off, PostgreSQL 17, `vector` and `postgis` allowed), the storage account with its private containers (`originals`, `derivatives`, `manifests`, `incoming`, `exports`, `dataprotection`), Key Vault, the container registry (Basic), Application Insights on `sepiabox-logs`, the Container Apps environment, the two managed identities (web and jobs), the Service Bus namespace `sepiabox-sb` (Basic) with its `manifests` queue, and the Event Grid system topic that sends new manifests to it (S-033). The first run asks nothing: azd generates the PostgreSQL login and password and keeps them in `.azure/`.
2. `azd deploy migrate`: builds the worker image and updates the `sepiabox-migrate` Container Apps job (manual trigger, 10 minutes, no retries).
3. Starts one execution of `sepiabox-migrate` (`sepiabox-worker migrate` against the cloud database) and waits for it, up to 15 minutes.
4. Only if it succeeded, `azd deploy import` and `azd deploy import-all`: the import jobs (S-033), which expect the new schema and never migrate (D-063).
5. Then `azd deploy web`: builds the web image and creates the `sepiabox-web` revision (0–1 replicas, HTTPS ingress, `AllowedHosts` = its default host).

If the migration fails, is stopped or times out, the script stops before step 4 and the running web is left as it was; the job's logs are in the `sepiabox-logs` workspace (table `ContainerAppConsoleLogs_CL`, container `migrate`). Fix the cause and run the script again: migrations already applied are skipped. `azd up` and a bare `azd deploy` are refused by the `predeploy` hook in `azure.yaml`, since they would start the web before the migrations.

**Keep the PostgreSQL login and password.** azd generates them once, on the first provision, and keeps them only in `.azure/sepiabox/config.json` (under `infra.parameters`, as `pg_username` and `pg_password`). Losing that folder does not regenerate them harmlessly: a new azd environment would generate a new login, which cannot be applied to the existing server, so provisioning fails; a new password alone would be applied, and the migrate job would fail to connect until its Key Vault reference refreshes (up to 30 minutes). So after the first provision, copy both values into a password manager (or a backup of `.azure/sepiabox/config.json`). They can also be read back from the secret `connectionstrings--catalog` (`Username=` and `Password=`) in the **database vault** (`databasevault-…`, which only the migrate job reads, D-062), which needs a Key Vault data role such as *Key Vault Secrets User* on that vault for your account: Owner on the resource group alone cannot read secrets. The third generated value, `pg_app_password` (the web's application role), needs no backup: if it is lost, the next deploy generates a new one and the migrate job sets it on the role before the web's new revision starts. If the first provision rejects the generated password (Azure's password rules), set a new one with `azd env config set infra.parameters.pg_password <letters and digits, with upper and lower case and a digit>` and provision again. To restore them on a new computer: `azd env new sepiabox --location francecentral --subscription <subscription id>`, then `azd env config set infra.parameters.pg_username <login>` and `azd env config set infra.parameters.pg_password <password>` (check the key names against your backup), and only then run the script. S-034's workflow supplies the same two values from GitHub secrets.

**The deploy hook is a guard, not a lock.** It only stops an accidental `azd up` or `azd deploy`; anyone who sets `SEPIABOX_DEPLOY_SCRIPT=1` by hand bypasses it. On Windows the hook runs in PowerShell 7 (`pwsh`), which must be installed; the script itself needs Bash.

**If azd cannot deploy one resource at a time.** The script relies on `azd deploy migrate`, `azd deploy import`, `azd deploy import-all` and `azd deploy web` each deploying a single AppHost resource, and on azd deploying Container Apps jobs; neither could be tried before the first real deploy. If either is refused or a job is not created, deploy by hand in the same order:

1. `export SEPIABOX_DEPLOY_SCRIPT=1 && azd provision --no-prompt`, then pause the queue as the script does: `az servicebus queue update --resource-group rg-sepiabox --namespace-name sepiabox-sb --name manifests --status ReceiveDisabled`.
2. Generate the modules: `dotnet run --project src/Sepiabox.AppHost -- --publisher manifest --output-path .azure/publish`.
3. For each of `migrate` (project `src/Sepiabox.Worker`), then `import` and `import-all` (the same project), then `web` (project `src/Sepiabox.Web`), in that order:
   1. Build and push its image: `az acr login --name <registry name>` (from `azd env get-values`, `AZURE_CONTAINER_REGISTRY_ENDPOINT` without `.azurecr.io`), then `dotnet publish <project> -c Release -t:PublishContainer -p:ContainerRegistry=<registry endpoint> -p:ContainerRepository=sepiabox/<resource> -p:ContainerImageTag=$(git rev-parse --short HEAD)`.
   2. Create or update it: `az deployment group create --resource-group rg-sepiabox --template-file .azure/publish/<resource>-containerapp.module.bicep --parameters <resource with - as _>_containerimage=<registry endpoint>/sepiabox/<resource>:<tag> ...`, giving every other parameter of the module its value from `azd env get-values`: the `AZURE_CONTAINER_APPS_ENVIRONMENT_*` and `AZURE_CONTAINER_REGISTRY_*` values give the `aca_outputs_*` parameters, and the vault, storage, access and identity outputs give the rest.
   3. After `migrate` only: run the job and wait for it as the script does (`az containerapp job start --name sepiabox-migrate --resource-group rg-sepiabox`, then `az containerapp job execution show ... --query properties.status` until `Succeeded`); stop here if it fails.
   4. After `import-all`: make the queue receive again (`... --status Active`).

After the first deploy, record here which of the two behaviours azd showed (single-resource deploys and the job worked, or the manual steps were needed), so the script can be fixed if needed.

Until the owner creates the secrets below, the web starts with no sign-in buttons and lets no one in, but `/health` answers. On the first deploy the web may restart a few times while its new role assignments reach Key Vault (a few minutes); it then starts normally. The web's address is `https://sepiabox-web.<environment default domain>`: `az containerapp show --name sepiabox-web --resource-group rg-sepiabox --query properties.configuration.ingress.fqdn -o tsv`.

## Deploying from GitHub Actions

`.github/workflows/deploy.yml` (S-034, D-064) deploys `main` after CI passes on a push to it, and on demand (Actions → Deploy → Run workflow). It waits for approval in the `production` environment, signs in with OIDC (no secret), recreates the azd environment `sepiabox` from the saved parameters, runs `scripts/deploy.sh` (so a failed migration stops it before the import jobs and the web), and then keeps the last 5 images of every repository in the container registry. It never runs for pull requests. A run whose commit is no longer the newest on `main` ends in its first job, without asking for approval and without deploying, because the newer commit's own run deploys it. If that newer commit's CI fails, nothing is deployed until `main` is green again: fix `main`, or run Deploy by hand once it is green.

**The first deploy is from your computer** (`./scripts/deploy.sh`, see "Deploying to Azure"): azd generates the PostgreSQL parameters there, once. Then copy them into the `production` environment's secrets, straight from your `.azure` backup to GitHub, without pasting them anywhere (you need the GitHub CLI, `gh auth login`):

```bash
cd sepiabox-platform   # the folder with .azure/sepiabox/config.json
set -o pipefail
for pair in PG_USERNAME:pg_username PG_PASSWORD:pg_password PG_APP_PASSWORD:pg_app_password; do
  # -e makes jq fail on a missing key instead of printing "null"; the secret is set only when it succeeded.
  if value=$(jq -er ".infra.parameters.${pair#*:}" .azure/sepiabox/config.json); then
    printf '%s' "$value" | gh secret set "${pair%%:*}" --env production --repo sepiabox/sepiabox-platform
  else
    echo "Not set: ${pair%%:*} (check the key names in config.json)" >&2
  fi
done
unset value
gh variable set UPLOADER_PRINCIPAL_IDS --env production --repo sepiabox/sepiabox-platform \
  --body "$(jq -r '.infra.parameters.uploaderPrincipalIds // ""' .azure/sepiabox/config.json)"
```

`gh secret set` reads the value from its input, so it never appears on the screen or in the shell history. Without the `.azure` folder, the login and password can be read from the database vault's secret `connectionstrings--catalog` instead (it needs *Key Vault Secrets User* on that vault for your account):

```bash
vault=$(az keyvault list --resource-group rg-sepiabox --query "[?starts_with(name, 'databasevault-')].name | [0]" -o tsv)
az keyvault secret show --vault-name "$vault" --name connectionstrings--catalog --query value -o tsv \
  | sed -n 's/.*Username=\([^;]*\).*/\1/p' | gh secret set PG_USERNAME --env production --repo sepiabox/sepiabox-platform
az keyvault secret show --vault-name "$vault" --name connectionstrings--catalog --query value -o tsv \
  | sed -n 's/.*Password=\([^;]*\).*/\1/p' | gh secret set PG_PASSWORD --env production --repo sepiabox/sepiabox-platform
```

`PG_APP_PASSWORD` (the web's application role) is not kept in Key Vault; if it is lost, set a new one of 64 letters and digits, which the next deploy's migrate job gives the role before the web restarts: `LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c 64 | gh secret set PG_APP_PASSWORD --env production --repo sepiabox/sepiabox-platform`. The workflow stops before azd if any of the three is missing, so azd never generates new ones on a runner (D-061).

After a workflow deploy, check the run's "Purge old images" step: one `acr purge` per repository, each keeping 5 images.

## Custom domain

The web answers at `https://app.sepiabox.com` once two azd parameters are set (S-035, D-065): `customDomain` (empty by default: no custom domain at all) and `customDomainBinding` (`Disabled` by default). Azure issues the free managed certificate only for a host name that is already on the app and whose CNAME points at it, so it takes two deploys, in this order:

1. **Find the two values.** The web's host: `az containerapp show --name sepiabox-web --resource-group rg-sepiabox --query properties.configuration.ingress.fqdn -o tsv`. The verification id: `az containerapp env show --resource-group rg-sepiabox --name "$(az containerapp env list --resource-group rg-sepiabox --query "[0].name" -o tsv)" --query properties.customDomainConfiguration.customDomainVerificationId -o tsv` (also the environment's output `AZURE_CONTAINER_APPS_CUSTOM_DOMAIN_VERIFICATION_ID`, in `azd env get-values` if azd keeps it).
2. **DNS at GoDaddy** (domain `sepiabox.com`): a CNAME record, name `app`, value the web's host from step 1; a TXT record, name `asuid.app`, value the verification id. Wait until `dig +short CNAME app.sepiabox.com` and `dig +short TXT asuid.app.sepiabox.com` return them.
3. **Add the host name.** Set `customDomain` to `app.sepiabox.com`, keeping `customDomainBinding` at `Disabled`. Always set the GitHub variables `CUSTOM_DOMAIN` (`app.sepiabox.com`) and `CUSTOM_DOMAIN_BINDING` (`Disabled`), even when you deploy from your computer: the Deploy workflow rebuilds the azd environment from them on every run, so a value set only locally is removed by the next workflow deploy. Locally also `azd env config set infra.parameters.customDomain app.sepiabox.com`. The workflow accepts only a plain host name and `Disabled` or `SniEnabled` (in any case), and stops otherwise. Deploy (`./scripts/deploy.sh` or the Deploy workflow). Check: `az containerapp hostname list --name sepiabox-web --resource-group rg-sepiabox -o table` lists `app.sepiabox.com` with binding `Disabled`. A deploy that fails here with a domain verification error means the TXT record is missing or not yet visible.
   **Before step 4:** sepiabox-infra's "Allowed resource types" policy must allow `Microsoft.App/managedEnvironments/managedCertificates` (sepiabox/sepiabox-infra#6) and sepiabox-infra's Deploy workflow must have run since; otherwise the policy refuses the certificate and the provision fails.
4. **Create and bind the certificate.** Set the GitHub variable `CUSTOM_DOMAIN_BINDING` to `SniEnabled` (and locally `azd env config set infra.parameters.customDomainBinding SniEnabled`) and deploy again: provisioning creates the managed certificate `sepiabox-custom-domain` (it can take several minutes), then the web binds it. Check: `az containerapp env certificate list --resource-group rg-sepiabox --name <environment> --managed-certificates-only -o table` shows it `Succeeded`, and the host name shows `SniEnabled`. On the next two deploys, check that the certificate is left alone: `az containerapp env certificate list --resource-group rg-sepiabox --name <environment> --managed-certificates-only --query "[].{state:properties.provisioningState,thumbprint:properties.thumbprint}" -o table` still shows `Succeeded` and the same thumbprint; report it if either changes. If the certificate fails, check that the CNAME points at the web's host from step 1 and that no CAA record on `sepiabox.com` forbids DigiCert, then deploy again.
5. **OAuth.** Add `https://app.sepiabox.com/signin-google` and `/signin-microsoft` to the Google and Microsoft OAuth clients (see "OAuth redirect URLs"), then run the S-035 owner check.

To remove the domain, set `CUSTOM_DOMAIN` (and `customDomain` locally) back to empty and `CUSTOM_DOMAIN_BINDING` to `Disabled`, and deploy. Deleting the certificate afterwards is optional (`az containerapp env certificate delete`); an unused managed certificate costs nothing. Browsers that visited the app keep using HTTPS for it for a year (HSTS, D-065), which is harmless since the app never serves plain HTTP.

## Secrets in Azure

There are two vaults in `rg-sepiabox` (D-062). The **owner's vault** (`vault-…`) holds what only the owner can provide; the web reads every secret in it as configuration, `--` standing for `:`, and reads them again every 5 minutes. The **database vault** (`databasevault-…`) holds the administrator's connection strings, written by the deploy and read only by the migrate job; never put anything there.

The deploy never creates the owner's secrets. Once, give yourself the right to write them (Owner on the resource group grants no data access):

```bash
vault=$(az keyvault list --resource-group rg-sepiabox --query "[?starts_with(name, 'vault-')].name | [0]" -o tsv)
az role assignment create --role "Key Vault Secrets Officer" --assignee "$(az ad signed-in-user show --query id -o tsv)" \
  --scope "$(az keyvault show --name "$vault" --query id -o tsv)"
```

Then set the secrets (a provider without both of its keys is left out, as locally):

```bash
az keyvault secret set --vault-name "$vault" --name Auth--Google--ClientId --value "<client id>"
az keyvault secret set --vault-name "$vault" --name Auth--Google--ClientSecret --value "<client secret>"
az keyvault secret set --vault-name "$vault" --name Auth--Microsoft--ClientId --value "<client id>"
az keyvault secret set --vault-name "$vault" --name Auth--Microsoft--ClientSecret --value "<client secret>"
az keyvault secret set --vault-name "$vault" --name Access--Emails --value "<e-mail>,<e-mail>"
az keyvault secret set --vault-name "$vault" --name Anthropic--ApiKey --value "<api key>"
```

When a secret applies: `Access--Emails` (and `Anthropic--ApiKey`, which the web reads when it uses it) within 5 minutes, when the web reads Key Vault again; the `Auth--*` sign-in secrets only when the web starts, so after creating or changing them restart the revision (`az containerapp revision restart --name sepiabox-web --resource-group rg-sepiabox --revision <latest revision>`), deploy a new revision, or wait for the web to scale from zero.

On the first deploy, and whenever the web's identity is new, its role on the vault takes a few minutes to propagate: until then the web cannot read Key Vault at start and fails, and Container Apps restarts it a few times before it starts normally. Do the owner checks only after that. Keep the values out of your shell history (for example, read them with `read -s`).

## MVP acceptance on the real data

After uploading the drive to Azure (below) and the automatic import finishing, run `sepiabox-ingest report <workdir>...` on every work folder you have uploaded, in the order you uploaded them (an asset in several folders counts once and takes its date from the last one, as the cloud does): it counts unique assets per year and kind, and the undated ones, from its manifests, the same unit as the catalog (an exact duplicate counts once; `scan` counts files instead). Then open the app and ask, in English and in Portuguese: photos per year for three years, videos per year for two years, and how many items have no date. Each answer must equal `report`'s number. A difference is reported as an issue with the question, both numbers and the year. Cities are not part of this check (`report` has no place names). No test data is ever uploaded to Azure for this (S-054, D-056).

## Automatic import

Every manifest `upload` writes is imported without anything else to run (S-033, D-063): Event Grid sends the storage account's BlobCreated events for the `manifests` container to the `manifests` queue of `sepiabox-sb` (Service Bus Basic), and the `sepiabox-import` job, woken by KEDA within about 30 seconds of a message, imports until the queue is empty (one execution at a time, up to 30 minutes, then it scales back to zero). When an import fails because of the database or storage, the execution ends and the message comes back when its 5-minute lock expires, so its 5 deliveries are spread over about 25 minutes, which rides out a database restart; the fifth failure moves it to the dead-letter queue with the reason. A manifest that is itself invalid is logged and not retried. During a deploy the queue keeps its messages but nothing receives them, from after provisioning until the import jobs run the new image (`scripts/deploy.sh` makes it receive again on the way out, also when the migration fails; only when the migration succeeded and the import jobs could not be updated does it leave the queue paused and print the command to resume it once they run the new image).

**Backfill and reconcile.** `sepiabox-import-all` imports every new or changed manifest in the container (`import --once`; what is already imported is skipped). Run it once after the first deploy, for manifests uploaded before this story, and whenever the dead-letter count is not zero or the queue was disabled (the €45 shutdown disables it; Event Grid then retries for 24 hours and gives up). Don't run it while an upload is being imported: both jobs could insert the same new asset at once; the event-driven job then lets the message come back, but `import-all` reports that manifest as a problem until its next run.

```bash
az containerapp job start --name sepiabox-import-all --resource-group rg-sepiabox
az containerapp job execution list --name sepiabox-import-all --resource-group rg-sepiabox --query "[0].properties.status" -o tsv
```

**Dead letters.** Check the count:

```bash
az servicebus queue show --resource-group rg-sepiabox --namespace-name sepiabox-sb --name manifests --query countDetails.deadLetterMessageCount
```

When it is not `0`: find why in the job's logs (the `sepiabox-logs` workspace, table `ContainerAppConsoleLogs_CL`, container `import`; a database that was stopped or a failed deploy are the usual causes), fix the cause, then run `sepiabox-import-all`, which imports whatever the dead-lettered messages named. Then empty the dead-letter queue: Azure portal → `sepiabox-sb` → Queues → `manifests` → Service Bus Explorer → Dead-letter → Purge (your account needs *Azure Service Bus Data Owner* on the namespace for it). A manifest reported as invalid by both jobs needs a new `process` and `upload` of its file.

**Check after the first deploy that the job starts.** The job's scale rule reads the queue's length as the import identity (`import-identity`), which has *Azure Service Bus Data Owner* on the `manifests` queue only: reading the length needs the Manage claim. Upload one small folder; within about 30 seconds `az containerapp job execution list --name sepiabox-import --resource-group rg-sepiabox --query "[0].{status:properties.status,start:properties.startTime}" -o table` shows a new execution. If none appears while messages wait (`az servicebus queue show --resource-group rg-sepiabox --namespace-name sepiabox-sb --name manifests --query countDetails.activeMessageCount`), look for scale rule errors (KEDA, `azure-servicebus`, 401 or Unauthorized) in the `sepiabox-logs` workspace, table `ContainerAppSystemLogs_CL`, for `sepiabox-import`, and report them; meanwhile `sepiabox-import-all` imports the waiting manifests.

**Check after the first deploy how failures are retried.** The job leaves a failed message locked so that it comes back only when its 5-minute lock expires (D-063); whether ending the execution releases the lock earlier is not verified. Once, at a quiet moment: stop PostgreSQL (`az postgres flexible-server stop --name sepiabox-pg --resource-group rg-sepiabox`), upload one small folder, wait about 15 minutes, start PostgreSQL again (`az postgres flexible-server start ...`), then list the import executions (`az containerapp job execution list --name sepiabox-import --resource-group rg-sepiabox --query "[].{status:properties.status,start:properties.startTime}" -o table`). Failed executions about 5 minutes apart confirm the assumption, and the message is imported once PostgreSQL is back. Executions about 30 seconds apart mean the lock is released when the receiver closes: report it, so the job can wait for its lock to expire before exiting, or defer the message instead. Check the dead-letter count afterwards.

KEDA may also start short executions that find nothing to receive, or fail, while a message is locked or the queue is paused during a deploy; they cost seconds and need no action.

**First provision.** If the first `azd provision` fails on the event subscription `manifests-to-queue` with an authorization error, the system topic's new role on the queue has not reached Event Grid yet: wait a few minutes and run the script again.

**Names.** A storage account can have only one Event Grid system topic (`sepiabox-storage-events`); if one already exists on the account, delete it or reuse its name in `Bicep/events.bicep`. `sepiabox-sb` is a global name: if Azure reports it as taken, change it in `AzureInfrastructure.ServiceBusName` here and in the shutdown runbook's `ServiceBusNamespace` in sepiabox-infra (`automation/Stop-Sepiabox.ps1`, `scripts/resume.sh`, `scripts/emergency-stop.sh`) in the same change, with the test constant `ShutdownRunbook.ServiceBusNamespace`.

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

A provider without both of its keys is left out (its button is not shown); one key without the other stops the web from starting. A change to `Access:Emails` takes effect when the configuration reloads: locally when the user secrets file changes, in Azure within 5 minutes, when the web reads Key Vault again (S-058). Someone removed from the list loses access on their next request, and an open page within 5 minutes. Locally:

```bash
dotnet user-secrets set "Access:Emails" "<e-mail>,<e-mail>" --project src/Sepiabox.Web
dotnet user-secrets set "Auth:Google:ClientId" "<client id>" --project src/Sepiabox.Web
dotnet user-secrets set "Auth:Google:ClientSecret" "<client secret>" --project src/Sepiabox.Web
```

Without any OAuth client, in Development, `/sign-in` shows an e-mail field instead (it posts to `/dev-login`): it signs in any e-mail on `Access:Emails` without a password, only from the same computer. `/dev-login` answers 404 in every other environment, and also in Development once a client is configured.

## OAuth redirect URLs

- Local: `https://localhost:7180/signin-google`, `/signin-microsoft` (the `https` port in `src/Sepiabox.Web/Properties/launchSettings.json`)
- Azure, before the custom domain: `https://<Container Apps default host>/signin-google`, `/signin-microsoft` (S-032)
- Production: `https://app.sepiabox.com/signin-google`, `/signin-microsoft` (S-035). Add them next to the default host's, and keep those: the default host keeps answering (it is in `AllowedHosts` too), and it is the way back if the domain's DNS or certificate fails.

## Emergency shutdown

See `sepiabox-infra/scripts/emergency-stop.sh` (turns off web traffic, stops PostgreSQL and disables the queue; keeps the data). After `resume.sh`, run `sepiabox-import-all` (see "Automatic import") for the manifests uploaded while the queue was disabled.
