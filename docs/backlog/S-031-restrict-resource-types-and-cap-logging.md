## S-031 — Restrict resource types and cap logging
**Epic:** Cloud and costs
**Depends on:** S-029
**Priority:** High
**Size:** S

### Context
An expensive resource created by mistake, or too much telemetry, could blow the ceiling. **Repository: `sepiabox-infra`.**

### Scope
- `guardrails/main.bicep`: Azure Policy "Allowed resource types" on `rg-sepiabox` with only the architecture's types (Container Apps and environment, PostgreSQL flexible, Storage, Service Bus, Event Grid, Key Vault, Log Analytics, Application Insights, managed identities, Automation, Container Registry, Cognitive Services/Foundry, Network if required by Container Apps).
- Log Analytics workspace with a daily cap of 0.15 GB and 30-day retention (used by App Insights in S-032).

### Out of scope
- Additional security policies.

### Acceptance criteria
- [ ] Given the compiled JSON, when it is inspected, then the policy lists exactly the allowed types.
- [ ] Given the workspace, when it is inspected, then `dailyQuotaGb` is 0.15 and retention is 30 days.

### Technical notes
- Export the workspace id as an output for the AppHost (S-032).

- Update (2026-10-20): Cognitive Services/Foundry was removed from the allowed types in `sepiabox-infra` (sepiabox/sepiabox-infra#5, merged), since Claude is called through the Anthropic API (D-052). No follow-up needed.

### Expected tests
- CI: Bicep compilation and assertions on the compiled JSON.
