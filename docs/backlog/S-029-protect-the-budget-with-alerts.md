## S-029 — Protect the budget with alerts
**Epic:** Cloud and costs
**Depends on:** none
**Priority:** High
**Size:** S

### Context
The ceiling is €50/month and Azure does not block spending on pay-as-you-go (D-008). The alerts exist before any paid resource. **Repository: `sepiabox-infra`.**

### Scope
- `budget/main.bicep` (subscription scope): monthly budget of 50 in the billing currency; actual-cost notifications at 50% (€25) and 80% (€40) and a forecast notification at 100%, to e-mail addresses passed as a parameter; daily cost anomaly alert.
- Workflow `deploy-budget.yml` (manual and on push to `main` under `budget/**`) with `azure/login` via OIDC.
- CI on PRs: `az bicep build` and a test that reads the compiled JSON and confirms amount 50 and thresholds 50/80/100.

### Out of scope
- Automatic shutdown (S-030).

### Acceptance criteria
- [ ] Given the Bicep, when CI compiles it, then there are no errors.
- [ ] Given the compiled JSON, when it is inspected, then the budget is 50 with thresholds 50, 80 (actual) and 100 (forecast).
- [ ] Given the workflow, when it is inspected, then it uses OIDC and has no plaintext secrets.

### Technical notes
- Resources: `Microsoft.Consumption/budgets`, `Microsoft.CostManagement/scheduledActions` (kind `InsightAlert`).
- Human prerequisite: subscription and OIDC credential (`docs/operations.md` in `sepiabox-platform`).

### Expected tests
- CI: Bicep compilation and assertions on the compiled JSON (PowerShell script or bash with `jq`).
