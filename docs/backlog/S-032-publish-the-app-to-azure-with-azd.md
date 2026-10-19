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
- `azure.yaml`; deploy steps in `docs/operations.md`.

### Out of scope
- Event triggering (S-033), CI/CD (S-034), domain (S-035).

### Acceptance criteria
- [ ] Given `azd infra synth` (or AppHost publishing), when the generated Bicep is inspected, then it has PostgreSQL B1ms, auto-grow off, region `francecentral` and `allowBlobPublicAccess: false` (verified by a test).
- [ ] Given the generated Bicep, when it is inspected, then the web app has `minReplicas: 0`.
- [ ] Given a real (manual) deploy, when I call `GET /health`, then it responds 200.

### Technical notes
- Customizations via Aspire's `ConfigureInfrastructure`.
- Human prerequisites in `docs/operations.md`.
- Decisions: D-002, D-003, D-004, D-022.

### Expected tests
- A test that generates the infrastructure and checks the properties in the Bicep. Manual smoke test after deploy.
