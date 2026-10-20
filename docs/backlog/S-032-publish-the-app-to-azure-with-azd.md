## S-032 — Publish the app to Azure with azd
**Epic:** Cloud and costs
**Depends on:** S-010, S-031
**Priority:** High
**Size:** M

### Context
The AppHost describes the infrastructure; `azd` provisions it in France Central with the cheap and secure settings already decided (D-002, D-003, D-004, D-022).

### Scope
- AppHost for Azure: PostgreSQL Flexible B1ms Burstable, 32 GB, storage auto-grow off, PostgreSQL 17, extensions `vector` and `postgis` allowed (`azure.extensions`); Storage with containers `originals`, `derivatives`, `manifests`, `incoming`, `exports` and public access off; Container Apps environment; web with 0–1 scale and external ingress; Key Vault (OAuth secrets, `Access:Emails`, Foundry key); App Insights wired to the S-031 workspace; Container Registry Basic.
- Web managed identity: Storage Blob Data Contributor (user delegation SAS) and Key Vault Secrets User.
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
- [ ] Given the generated infrastructure, when it is inspected, then `Auth:Google:ClientId`, `Auth:Google:ClientSecret`, `Auth:Microsoft:ClientId`, `Auth:Microsoft:ClientSecret` and `Access:Emails` reach the web as Key Vault references or Aspire secret parameters, never as plain values.
- [ ] Given a real (manual) deploy, when I sign in with Google on `https://app.sepiabox.com`, restart the web revision and reload the page, then I am still signed in; and an account not on `Access:Emails` lands on `/sem-acesso` (steps in `docs/operations.md`).

### Technical notes
- Customizations via Aspire's `ConfigureInfrastructure`.
- Human prerequisites in `docs/operations.md`.
- The web signs blob links with a user delegation key (D-039): its managed identity needs *Storage Blob Data Reader* on the storage account. That role includes the user delegation key action, so *Storage Blob Delegator* is not added.
- Sign-in (S-010, D-048): the web already reads forwarded headers outside Development; S-032 adds the data protection key ring (a blob in the storage account, a key in Key Vault; the web identity needs *Key Vault Crypto User* on that key and write access to that blob) and wires the sign-in secrets.
- Decisions: D-002, D-003, D-004, D-022, D-034, D-048.

### Expected tests
- A test that generates the infrastructure and checks the properties in the Bicep. Manual smoke test after deploy.
