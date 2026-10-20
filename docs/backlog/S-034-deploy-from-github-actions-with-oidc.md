## S-034 — Deploy from GitHub Actions with OIDC
**Epic:** Cloud and costs
**Depends on:** S-032
**Priority:** Medium
**Size:** S

### Context
Repeatable deploys, with no secrets in the repository and never running on third-party PRs (public repository).

### Scope
- `.github/workflows/deploy.yml`: on push to `main` (after CI is green) and manual; `production` environment; `permissions: id-token: write, contents: read`; `azure/login` OIDC with variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`; `azd deploy`.
- Never on `pull_request` or `pull_request_target`.
- `actionlint` in the CI lint job.

### Out of scope
- Additional environments.

### Acceptance criteria
- [x] Given the workflow, then `actionlint` passes.
- [x] Given the workflow, then it uses the `production` environment, minimal permissions and no PR trigger.
- [x] Given the repository, then there is no Azure secret in plain text.
- [x] Given the workflow, then after a successful deploy it runs `az acr run --registry <registry> --cmd "acr purge --filter 'sepiabox-web:.*' --keep 5 --untagged" /dev/null` and the same for `sepiabox-worker`, so the registry keeps the last 5 images of each repository (a test on the workflow); the deploy identity needs *AcrDelete* (or Contributor) on the registry, listed in `docs/operations.md`.
- [x] Given the workflow, then it runs the `sepiabox-worker migrate` job (S-032) and waits for its success before the web is deployed; a failed migration stops the deploy (a test on the workflow or the `azure.yaml` hooks it calls).

### Technical notes
- Configuring the environment and the federated credential is a human step (`docs/operations.md`).
- Update (S-032, D-061): deploy by running `scripts/deploy.sh`, not a bare `azd deploy` (the `predeploy` hook refuses it); the script already runs the migrate job and waits for it. The workflow creates the azd environment `sepiabox` in `francecentral` and supplies the PostgreSQL administrator login and password from GitHub secrets (`PG_USERNAME`, `PG_PASSWORD`, set by the owner from the values azd generated on the first deploy) with `azd env config set infra.parameters.pg_username` / `pg_password`, since a newly generated login cannot be applied to the existing server. Check the purge filters against the repository names azd actually pushes.

### Expected tests
- `actionlint` and a test that parses the YAML (triggers, permissions, environment).
