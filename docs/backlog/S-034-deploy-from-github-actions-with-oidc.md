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
- [ ] Given the workflow, then `actionlint` passes.
- [ ] Given the workflow, then it uses the `production` environment, minimal permissions and no PR trigger.
- [ ] Given the repository, then there is no Azure secret in plain text.

### Technical notes
- Configuring the environment and the federated credential is a human step (`docs/operations.md`).

### Expected tests
- `actionlint` and a test that parses the YAML (triggers, permissions, environment).
