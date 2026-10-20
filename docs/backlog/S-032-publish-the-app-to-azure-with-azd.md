## S-032 — Publish the app to Azure with azd
**Epic:** Cloud and costs
**Depends on:** S-010, S-031
**Priority:** High
**Size:** M

### Context
The AppHost describes the infrastructure; `azd` provisions it in France Central with the cheap and secure settings already decided (D-002, D-003, D-004, D-022). This story creates the resources, the migrations job and a healthy web; identities, roles and secrets are S-058.

### Scope
- AppHost for Azure: PostgreSQL Flexible B1ms Burstable, 32 GB, storage auto-grow off, PostgreSQL 17, extensions `vector` and `postgis` allowed (`azure.extensions`); Storage with containers `originals`, `derivatives`, `manifests`, `incoming`, `exports`, `dataprotection` and public access off; Container Apps environment; web with 0–1 scale and external ingress; Key Vault; App Insights wired to the S-031 workspace; Container Registry Basic.
- Resource names the €45 shutdown runbook (S-030) stops: the web Container App is `sepiabox-web` and the PostgreSQL server `sepiabox-pg` (set explicitly in `ConfigureInfrastructure`, not left to generated names).
- Database migrations in the cloud (D-034): a Container Apps job running `sepiabox-worker migrate` (manual trigger, replica timeout 10 min, no retries) with the catalog connection, run by the deploy before the new web revision receives traffic. The run-once `worker-migrate`/`worker-import` resources of the AppHost exist only in run mode and must stay out of publishing.
- `azure.yaml`; deploy steps in `docs/operations.md`.

### Out of scope
- Identities, roles, uploader access, data protection keys and Key Vault references (S-058).
- Event triggering (S-033), CI/CD (S-034), domain (S-035).

### Acceptance criteria
- [ ] Given `azd infra synth` (or AppHost publishing), when the generated Bicep is inspected, then it has PostgreSQL B1ms, auto-grow off, region `francecentral` and `allowBlobPublicAccess: false` (verified by a test).
- [ ] Given the generated Bicep, when it is inspected, then the web app has `minReplicas: 0`.
- [ ] Given the generated Bicep, then the web Container App is named `sepiabox-web` and the PostgreSQL server `sepiabox-pg`, the names S-030's runbook stops (a test compares them with the runbook's constants).
- [ ] Given the generated infrastructure, when it is inspected, then it has a Container Apps job that runs `sepiabox-worker migrate` with a manual trigger, and no always-on container app for the worker.
- [ ] Given a deploy, when the new web revision starts, then `sepiabox-worker migrate` has completed successfully against the cloud database first (deploy step order in `azure.yaml` or the deploy workflow).
- [ ] Given the generated infrastructure, when it is inspected, then the web is reached only through the Container Apps ingress (HTTPS only, no other exposed port), which the web's forwarded-header handling relies on (enabled by the web outside Development, D-048); verified by a test on the Bicep.
- [ ] Given a real deploy (manual, owner acceptance in `docs/operations.md`), when I call `GET /health` on the Container Apps default host, then it responds 200.

### Technical notes
- Customizations via Aspire's `ConfigureInfrastructure`.
- Human prerequisites in `docs/operations.md`.
- Sign-in (S-010, D-048): the web already reads `X-Forwarded-For` and `X-Forwarded-Proto` outside Development (never `X-Forwarded-Host`: the ingress keeps the browser's Host); set `AllowedHosts` to the Container Apps default host so no other Host reaches the app (S-035 adds `app.sepiabox.com`).
- Portability (D-051): storage keeps its current registration; existing storage code moves behind `IBlobStore` after the MVP (S-050). No Foundry or Cognitive Services resource is created (D-052).
- Videos (S-011, D-049): if a Content Security Policy is added, it must allow the blob endpoint in `media-src` as well as `img-src`, since the browser follows the web's redirect to storage for videos as for images.
- Localization (S-044, D-058): the web formats dates with ICU culture data (`InvariantGlobalization` is off for it), so its image must keep ICU: the default `aspnet` image has it; a chiseled image needs the `-extra` variant, and an Alpine one the ICU packages.
- Decisions: D-002, D-003, D-004, D-022, D-034, D-048, D-049, D-051, D-052, D-058.

### Expected tests
- A test that generates the infrastructure and checks the properties in the Bicep. Manual smoke test after deploy.
