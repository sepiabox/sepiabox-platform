## S-032 — Publish the app to Azure with azd
**Epic:** Cloud and costs
**Depends on:** S-010, S-031
**Priority:** High
**Size:** M

### Context
The AppHost describes the infrastructure; `azd` provisions it in France Central with the cheap and secure settings already decided (D-002, D-003, D-004, D-022).

### Scope
- AppHost for Azure: PostgreSQL Flexible B1ms Burstable, 32 GB, storage auto-grow off, PostgreSQL 17, extensions `vector` and `postgis` allowed (`azure.extensions`); Storage with containers `originals`, `derivatives`, `manifests`, `incoming`, `exports`, `dataprotection` and public access off; Container Apps environment; web with 0–1 scale and external ingress; Key Vault (OAuth secrets, `Access:Emails`, the Anthropic API key `Anthropic--ApiKey`, D-052); App Insights wired to the S-031 workspace; Container Registry Basic.
- Resource names the €45 shutdown runbook (S-030) stops: the web Container App is `sepiabox-web` and the PostgreSQL server `sepiabox-pg` (set explicitly in `ConfigureInfrastructure`, not left to generated names).
- Web managed identity: *Storage Blob Data Reader* on the storage account (reads, and the user delegation key for SAS links, D-039; so *Storage Blob Delegator* is not added), *Storage Blob Data Contributor* only on the `dataprotection` container (the data protection key ring is the web's only write), *Key Vault Secrets User*, and *Key Vault Crypto User* on the data protection key. The migrate job's identity: *Key Vault Secrets User* for the catalog connection.
- Uploader access: a parameter `uploaderPrincipalIds` (the Entra object ids of the people who run `sepiabox-ingest upload --destination azure`) gets *Storage Blob Data Contributor* on the storage account; Owner on the resource group grants no data access (`docs/operations.md`).
- Database migrations in the cloud (D-034): a Container Apps job running `sepiabox-worker migrate` (manual trigger, replica timeout 10 min, no retries) with the catalog connection, run by the deploy before the new web revision receives traffic. The run-once `worker-migrate`/`worker-import` resources of the AppHost exist only in run mode and must stay out of publishing.
- `azure.yaml`; deploy steps in `docs/operations.md`.

### Out of scope
- Event triggering (S-033), CI/CD (S-034), domain (S-035).

### Acceptance criteria
- [ ] Given `azd infra synth` (or AppHost publishing), when the generated Bicep is inspected, then it has PostgreSQL B1ms, auto-grow off, region `francecentral` and `allowBlobPublicAccess: false` (verified by a test).
- [ ] Given the generated Bicep, when it is inspected, then the web app has `minReplicas: 0`.
- [ ] Given the generated infrastructure, when it is inspected, then it has a Container Apps job that runs `sepiabox-worker migrate` with a manual trigger, and no always-on container app for the worker.
- [ ] Given a deploy, when the new web revision starts, then `sepiabox-worker migrate` has completed successfully against the cloud database first (deploy step order in `azure.yaml` or the deploy workflow).
- [ ] Given a real (manual) deploy, when I call `GET /health`, then it responds 200.
- [ ] Given the generated infrastructure, when it is inspected, then the web is reached only through the Container Apps ingress (HTTPS only, no other exposed port), which the web's forwarded-header handling relies on (enabled by the web outside Development, D-048); verified by a test on the Bicep.
- [ ] Given the web in Azure, when it starts, then its data protection keys are persisted to Blob Storage and protected with a Key Vault key (`PersistKeysToAzureBlobStorage` + `ProtectKeysWithAzureKeyVault`, managed identity), so a restart, a new revision or a second replica keeps everyone signed in (verified by a test of the web's service registration in the `Production` environment).
- [ ] Given the generated infrastructure, when it is inspected, then `Auth:Google:ClientId`, `Auth:Google:ClientSecret`, `Auth:Microsoft:ClientId`, `Auth:Microsoft:ClientSecret`, `Access:Emails` and `Anthropic:ApiKey` reach the web as Key Vault references or Aspire secret parameters, never as plain values.
- [ ] Given the generated Bicep, then the web Container App is named `sepiabox-web` and the PostgreSQL server `sepiabox-pg`, the names S-030's runbook stops (a test compares them with the runbook's constants).
- [ ] Given the generated Bicep, then the web identity has exactly the roles in Scope (Reader on the account, Contributor on `dataprotection` only), and each id in `uploaderPrincipalIds` has *Storage Blob Data Contributor* on the account.
- [ ] Given a real deploy (manual, owner acceptance in `docs/operations.md`), after adding the Container Apps default host's `/signin-google` and `/signin-microsoft` redirect URIs to the OAuth clients, when I sign in with Google on `https://<default host>`, restart the web revision and reload the page, then I am still signed in; and an account not on `Access:Emails` lands on the no-access page. (S-035 repeats this on `app.sepiabox.com`.)

### Technical notes
- Customizations via Aspire's `ConfigureInfrastructure`.
- Human prerequisites in `docs/operations.md`.
- Sign-in (S-010, D-048): the web already reads `X-Forwarded-For` and `X-Forwarded-Proto` outside Development (never `X-Forwarded-Host`: the ingress keeps the browser's Host); set `AllowedHosts` to the Container Apps default host so no other Host reaches the app (S-035 adds `app.sepiabox.com`). S-032 also adds the data protection key ring (a blob in the `dataprotection` container, a key in Key Vault; roles in Scope) and wires the sign-in secrets.
- Portability (D-051): the web and the worker read secrets through `IConfiguration` (Key Vault added as a configuration source or as Container Apps secret references, in the composition root only); storage keeps its current registration; existing storage code moves behind `IBlobStore` after the MVP (S-050). Data protection keys in Blob Storage and Key Vault are host wiring in `Program.cs`. No Foundry or Cognitive Services resource is created (D-052).
- Videos (S-011, D-049): if a Content Security Policy is added, it must allow the blob endpoint in `media-src` as well as `img-src`, since the browser follows the web's redirect to storage for videos as for images.
- Decisions: D-002, D-003, D-004, D-022, D-034, D-048, D-049, D-051, D-052.

### Expected tests
- A test that generates the infrastructure and checks the properties in the Bicep. Manual smoke test after deploy.
