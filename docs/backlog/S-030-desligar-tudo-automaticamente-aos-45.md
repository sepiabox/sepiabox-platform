## S-030 — Desligar tudo automaticamente aos €45
**Épico:** Nuvem e custos
**Depende de:** S-029
**Prioridade:** Alta
**Tamanho:** M

### Contexto
Os alertas avisam; um bug às 3 da manhã continua a gastar. Aos €45 (90%), um runbook desliga o que custa por uso e mantém os dados. **Repositório: `sepiabox-infra`.**

### Âmbito
- Automation Account (plano gratuito) com identidade gerida (Contributor em `rg-sepiabox`) e runbook PowerShell `Stop-Sepiabox.ps1`: desativa o ingress da web (`sepiabox-web`), para o PostgreSQL (`sepiabox-pg`) e desativa a fila (`sepiabox-sb/manifests`).
- Action group com webhook para o runbook; notificação de 90% no orçamento ligada ao action group.
- `scripts/resume.sh`: reativa ingress, arranca o PostgreSQL e reativa a fila.

### Fora de âmbito
- Apagar recursos.

### Critérios de aceitação
- [ ] Dado o Bicep compilado, então o orçamento tem uma notificação a 90% ligada ao action group.
- [ ] Dado o runbook com cmdlets Az simulados (Pester), então chama as três operações com os nomes certos.
- [ ] Dado `scripts/resume.sh`, então passa `bash -n` e `shellcheck`.

### Notas técnicas
- Reutilizar a lógica de `scripts/emergency-stop.sh`.

### Testes esperados
- Pester (mocks de `Az.*`), compilação Bicep, shellcheck no CI.
