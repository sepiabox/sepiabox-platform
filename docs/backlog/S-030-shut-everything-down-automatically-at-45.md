## S-030 — Shut everything down automatically at €45
**Epic:** Cloud and costs
**Depends on:** S-029
**Priority:** High
**Size:** M

### Context
Alerts warn; a bug at 3 a.m. keeps spending. At €45 (90%), a runbook shuts down what is billed by usage and keeps the data. **Repository: `sepiabox-infra`.**

### Scope
- Automation Account (free tier) with a managed identity (Contributor on `rg-sepiabox`) and PowerShell runbook `Stop-Sepiabox.ps1`: disables the web ingress (`sepiabox-web`), stops PostgreSQL (`sepiabox-pg`) and disables the queue (`sepiabox-sb/manifests`).
- Action group with a webhook to the runbook; 90% budget notification wired to the action group.
- `scripts/resume.sh`: re-enables ingress, starts PostgreSQL and re-enables the queue.

### Out of scope
- Deleting resources.

### Acceptance criteria
- [ ] Given the compiled Bicep, when it is inspected, then the budget has a 90% notification wired to the action group.
- [ ] Given the runbook with mocked Az cmdlets (Pester), when it runs, then it calls the three operations with the right names.
- [ ] Given `scripts/resume.sh`, when it is checked, then it passes `bash -n` and `shellcheck`.

### Technical notes
- Reuse the logic of `scripts/emergency-stop.sh`.

### Expected tests
- Pester (mocks of `Az.*`), Bicep compilation, shellcheck in CI.
