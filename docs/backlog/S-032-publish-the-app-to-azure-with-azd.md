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

### Technical notes
- Customizations via Aspire's `ConfigureInfrastructure`.
- Human prerequisites in `docs/operations.md`.
- The web signs blob links with a user delegation key (D-039): its managed identity needs *Storage Blob Data Reader* on the storage account. That role includes the user delegation key action, so *Storage Blob Delegator* is not added.
- Decisions: D-002, D-003, D-004, D-022, D-034.

### Expected tests
- A test that generates the infrastructure and checks the properties in the Bicep. Manual smoke test after deploy.
