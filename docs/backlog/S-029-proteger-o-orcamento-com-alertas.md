## S-029 — Proteger o orçamento com alertas
**Épico:** Nuvem e custos
**Depende de:** nenhuma
**Prioridade:** Alta
**Tamanho:** S

### Contexto
O teto é €50/mês e o Azure não bloqueia gastos em pagamento por uso (D-008). Os alertas existem antes de qualquer recurso pago. **Repositório: `sepiabox-infra`.**

### Âmbito
- `budget/main.bicep` (escopo subscrição): orçamento mensal de 50 na moeda de faturação; notificações reais a 50% (€25) e 80% (€40) e previsão a 100%, para e-mails passados por parâmetro; alerta diário de anomalia de custo.
- Workflow `deploy-budget.yml` (manual e em push para `main` em `budget/**`) com `azure/login` por OIDC.
- CI em PRs: `az bicep build` e um teste que lê o JSON compilado e confirma montante 50 e limiares 50/80/100.

### Fora de âmbito
- Desligamento automático (S-030).

### Critérios de aceitação
- [ ] Dado o Bicep, quando o CI compila, então não há erros.
- [ ] Dado o JSON compilado, então o orçamento é 50 com limiares 50, 80 (reais) e 100 (previsão).
- [ ] Dado o workflow, então usa OIDC e não tem segredos em texto.

### Notas técnicas
- Recursos: `Microsoft.Consumption/budgets`, `Microsoft.CostManagement/scheduledActions` (kind `InsightAlert`).
- Pré-requisito humano: subscrição e credencial OIDC (`docs/operations.md` do `sepiabox-platform`).

### Testes esperados
- CI: compilação Bicep e asserções sobre o JSON compilado (script em PowerShell ou bash com `jq`).
