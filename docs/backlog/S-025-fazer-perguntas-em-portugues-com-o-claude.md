## S-025 — Fazer perguntas em português com o Claude Haiku
**Épico:** Perguntas
**Depende de:** S-016, S-021, S-024, S-014
**Prioridade:** Média
**Tamanho:** M

### Contexto
A família escreve como fala ("fotos minhas com a Ana na neve em 2019"). O Claude Haiku traduz a pergunta para a DSL; só o texto sai do Azure France Central, nunca as fotos (D-007).

### Âmbito
- `IQuestionTranslator` com o SDK oficial C# da Anthropic (`Anthropic.Foundry`, `AnthropicFoundryClient`), modelo `claude-haiku-5-5`, structured outputs com o schema `search.v1.json`, `effort` low, `max_tokens` 1024.
- Prompt de sistema estável (documentação da DSL, exemplos) com prompt caching; contexto variável no fim: data de hoje, nomes de pessoas, lugares pessoais, fontes, tipos de ocasião.
- Termos visuais traduzidos para inglês em `visual.text` (D-017).
- UI: caixa de pergunta em `/`; mostra os filtros interpretados como chips editáveis e os resultados.
- Limite diário por utilizador (por defeito 200) → mensagem amigável e 429; regista tokens usados.
- `stop_reason` "refusal" ou JSON inválido → mensagem amigável, sem exceção.
- Configuração `Foundry:Resource`, `Foundry:ApiKey` (Key Vault em produção).

### Fora de âmbito
- Respostas com contagens (S-026).

### Critérios de aceitação
- [ ] Dado um tradutor falso que devolve uma DSL, quando pergunto, então os resultados e os chips correspondem a essa DSL.
- [ ] Dado o pedido construído, então usa o modelo `claude-haiku-5-5`, o schema da DSL como formato de saída e não contém imagens.
- [ ] Dado o contexto, então o prompt inclui os nomes das pessoas e lugares existentes.
- [ ] Dado JSON inválido ou recusa do modelo, então o utilizador vê uma mensagem e a app não falha.
- [ ] Dado o limite diário atingido, então a API devolve 429.

### Notas técnicas
- Antes de escrever código, ler o README C# do SDK da Anthropic para os nomes exatos (não adivinhar a API). Não usar prefill de assistente.
- Sem chamadas reais no CI; um teste manual opcional com chave real.
- Implantação no Foundry é pré-requisito humano (`docs/operations.md`).
- Decisões: D-007, D-017, D-020.

### Testes esperados
- Unitários: construção do prompt e do pedido, tratamento de recusa/JSON inválido, limite diário. Integração: endpoint com tradutor falso.
