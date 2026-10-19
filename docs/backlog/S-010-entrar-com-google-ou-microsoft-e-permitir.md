## S-010 — Entrar com Google ou Microsoft e permitir só a lista de acesso
**Épico:** App web
**Depende de:** S-009
**Prioridade:** Alta
**Tamanho:** M

### Contexto
O Sepiabox é privado para duas pessoas. O login prova a identidade; a lista de e-mails decide quem entra (D-006).

### Âmbito
- Autenticação por cookie + `AddGoogle` + `AddMicrosoftAccount`; configuração `Auth:Google:*`, `Auth:Microsoft:*`, `Access:Emails`.
- `OnTicketReceived`: e-mail fora da lista → redireciona para `/sem-acesso` e chama `HandleResponse()` (sem cookie).
- `FallbackPolicy`: autenticado e na lista, para todas as páginas e APIs; `/entrar`, `/sem-acesso` e `/health` anónimos.
- Página `/entrar` com dois botões; terminar sessão.
- Em `Development` sem clientes OAuth configurados: `/dev-login?email=` para testes locais (inexistente noutros ambientes).

### Fora de âmbito
- Apple (só com a app iOS).

### Critérios de aceitação
- [ ] Dado um pedido anónimo a `/`, então é redirecionado para `/entrar`.
- [ ] Dado um utilizador com e-mail na lista (maiúsculas diferentes incluídas), então acede a `/` com 200.
- [ ] Dado um e-mail fora da lista, então o handler de `OnTicketReceived` não cria sessão e redireciona para `/sem-acesso`.
- [ ] Dado um utilizador autenticado fora da lista com cookie antigo, então recebe 403 nas APIs.
- [ ] Dado o ambiente `Production`, então `/dev-login` devolve 404.

### Notas técnicas
- `Sepiabox.Web/Auth/AccessList.cs`, `AuthSetup.cs`. Testes com `WebApplicationFactory` e um esquema de autenticação de teste.
- Clientes OAuth reais são pré-requisito humano (`docs/operations.md`); os testes não dependem deles.
- Decisões: D-006.

### Testes esperados
- Unitários: `AccessList`, handler de ticket. Integração (WebApplicationFactory): redirecionamentos, 403, `/dev-login` por ambiente.
