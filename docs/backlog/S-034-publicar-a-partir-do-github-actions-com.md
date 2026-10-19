## S-034 — Publicar a partir do GitHub Actions com OIDC
**Épico:** Nuvem e custos
**Depende de:** S-032
**Prioridade:** Média
**Tamanho:** S

### Contexto
Deploys repetíveis, sem segredos no repositório e sem correr em PRs de terceiros (repositório público).

### Âmbito
- `.github/workflows/deploy.yml`: em push para `main` (depois do CI verde) e manual; ambiente `production`; `permissions: id-token: write, contents: read`; `azure/login` OIDC com variáveis `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`; `azd deploy`.
- Nunca em `pull_request` nem `pull_request_target`.
- `actionlint` no job de lint do CI.

### Fora de âmbito
- Ambientes adicionais.

### Critérios de aceitação
- [ ] Dado o workflow, então `actionlint` passa.
- [ ] Dado o workflow, então usa o ambiente `production`, permissões mínimas e nenhum gatilho de PR.
- [ ] Dado o repositório, então não existe nenhum segredo Azure em texto.

### Notas técnicas
- Configuração do ambiente e da credencial federada é humana (`docs/operations.md`).

### Testes esperados
- `actionlint` e um teste que analisa o YAML (gatilhos, permissões, ambiente).
