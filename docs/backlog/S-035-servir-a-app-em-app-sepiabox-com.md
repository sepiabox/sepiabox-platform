## S-035 — Servir a app em app.sepiabox.com
**Épico:** Nuvem e custos
**Depende de:** S-032
**Prioridade:** Baixa
**Tamanho:** S

### Contexto
O login do Google mostra o domínio; `app.sepiabox.com` inspira mais confiança do que o endereço gerado pelo Azure.

### Âmbito
- Parâmetro `customDomain` no AppHost: domínio personalizado + certificado gerido no Container App web quando definido.
- Cabeçalho HSTS em produção.
- `docs/operations.md`: registos DNS exatos (CNAME `app` → FQDN da app; TXT `asuid.app` → id de verificação) e URIs de retorno OAuth.

### Fora de âmbito
- Criar os registos DNS (passo humano na GoDaddy).

### Critérios de aceitação
- [ ] Dado `customDomain` definido, então o Bicep gerado tem o domínio e o certificado gerido.
- [ ] Dado `customDomain` vazio, então o Bicep não tem domínio personalizado.
- [ ] Dado um pedido em produção, então a resposta tem o cabeçalho `Strict-Transport-Security`.

### Notas técnicas
- Passo humano: DNS na GoDaddy.

### Testes esperados
- Teste de geração de infraestrutura; teste de middleware HSTS.
