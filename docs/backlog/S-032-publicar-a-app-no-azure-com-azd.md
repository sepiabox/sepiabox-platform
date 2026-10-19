## S-032 — Publicar a app no Azure com azd
**Épico:** Nuvem e custos
**Depende de:** S-010, S-031
**Prioridade:** Alta
**Tamanho:** M

### Contexto
O AppHost descreve a infraestrutura; o `azd` provisiona em France Central com as configurações baratas e seguras decididas (D-002, D-003, D-004, D-022).

### Âmbito
- AppHost para Azure: PostgreSQL Flexible B1ms Burstable, 32 GB, crescimento automático desligado, PostgreSQL 17, extensões `vector` e `postgis` permitidas (`azure.extensions`); Storage com contentores `originals`, `derivatives`, `manifests`, `incoming`, `exports` e acesso público desligado; ambiente Container Apps; web com escala 0–1 e ingress externo; Key Vault (segredos OAuth, `Access:Emails`, chave Foundry); App Insights ligado ao workspace de S-031; Container Registry Basic.
- Identidade gerida da web: Storage Blob Data Contributor (SAS de delegação) e Key Vault Secrets User.
- `azure.yaml`; passos de deploy em `docs/operations.md`.

### Fora de âmbito
- Disparo por eventos (S-033), CI/CD (S-034), domínio (S-035).

### Critérios de aceitação
- [ ] Dado `azd infra synth` (ou publicação do AppHost), então o Bicep gerado tem PostgreSQL B1ms, crescimento automático desligado, região `francecentral` e `allowBlobPublicAccess: false` (verificado por teste).
- [ ] Dado o Bicep gerado, então a web tem `minReplicas: 0`.
- [ ] Dado um deploy real (manual), então `GET /health` responde 200.

### Notas técnicas
- Personalizações com `ConfigureInfrastructure` do Aspire.
- Pré-requisitos humanos em `docs/operations.md`.
- Decisões: D-002, D-003, D-004, D-022.

### Testes esperados
- Teste que gera a infraestrutura e verifica as propriedades no Bicep. Smoke test manual após deploy.
