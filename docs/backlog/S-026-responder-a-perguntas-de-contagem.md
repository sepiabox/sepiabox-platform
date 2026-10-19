## S-026 — Responder a perguntas de contagem
**Épico:** Perguntas
**Depende de:** S-025
**Prioridade:** Média
**Tamanho:** M

### Contexto
"Quantos casamentos fomos este ano?" e "Com quem mais tirei fotos em 2025?" pedem números, não grelhas.

### Âmbito
- DSL: `aggregate: { "count": "events" | "assets", "groupBy": null | "person" | "year" | "eventType", "top": 10 }`.
- Executor devolve `{ total, groups: [{ key, count }] }`.
- UI: cartão de resposta (número grande + frase, ex.: "4 casamentos este ano") ou tabela de ranking, com ligação para os itens.
- Exemplos de contagem no prompt do tradutor.

### Fora de âmbito
- Somas de bytes (S-028).

### Critérios de aceitação
- [ ] Dado 4 ocasiões do tipo casamento em 2026, quando conto `events` com `eventTypes=[casamento]` e `years=[2026]`, então `total` é 4.
- [ ] Dado fotos com várias pessoas em 2025, quando agrupo por pessoa com `top=3`, então vêm as 3 com mais fotos, por ordem.
- [ ] Dado uma resposta de contagem, então o cartão mostra o número e a frase.

### Notas técnicas
- Estende `search.v1.json`, o executor e o prompt.

### Testes esperados
- Integração (PostgreSQL): contagens e agrupamentos. bUnit: cartão de resposta.
