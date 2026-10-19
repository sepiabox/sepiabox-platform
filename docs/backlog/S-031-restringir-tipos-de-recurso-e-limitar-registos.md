## S-031 — Restringir tipos de recurso e limitar registos
**Épico:** Nuvem e custos
**Depende de:** S-029
**Prioridade:** Alta
**Tamanho:** S

### Contexto
Um recurso caro criado por engano ou telemetria a mais podem estourar o teto. **Repositório: `sepiabox-infra`.**

### Âmbito
- `guardrails/main.bicep`: Azure Policy "Allowed resource types" no `rg-sepiabox` com só os tipos da arquitetura (Container Apps e ambiente, PostgreSQL flexible, Storage, Service Bus, Event Grid, Key Vault, Log Analytics, Application Insights, identidades geridas, Automation, Container Registry, Cognitive Services/Foundry, Network se exigido pelo Container Apps).
- Workspace Log Analytics com limite diário de 0,15 GB e retenção de 30 dias (usado pelo App Insights em S-032).

### Fora de âmbito
- Policies de segurança adicionais.

### Critérios de aceitação
- [ ] Dado o JSON compilado, então a policy lista exatamente os tipos permitidos.
- [ ] Dado o workspace, então `dailyQuotaGb` é 0,15 e a retenção 30 dias.

### Notas técnicas
- Exportar o id do workspace como output para o AppHost (S-032).

### Testes esperados
- CI: compilação Bicep e asserções sobre o JSON compilado.
