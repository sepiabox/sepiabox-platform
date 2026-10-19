## S-021 — Filtrar por pessoas
**Épico:** Pessoas
**Depende de:** S-020, S-013
**Prioridade:** Alta
**Tamanho:** S

### Contexto
"Fotos minhas com a Ana e o Pedro juntos" é a pergunta mais comum.

### Âmbito
- DSL: `people: { "all": ["Ana", "Pedro"], "includeMe": true }` (todas as pessoas na mesma foto; `includeMe` resolve "Sou eu" do utilizador atual).
- Seletor de pessoas na barra de filtros; o detalhe da foto lista as pessoas com ligação para o filtro.

### Fora de âmbito
- Agregações por pessoa (S-026).

### Critérios de aceitação
- [ ] Dado fotos com Ana, Pedro e ambos, quando filtro `all=[Ana,Pedro]`, então só vêm as que têm os dois.
- [ ] Dado `includeMe` para um utilizador com "Sou eu", então esse filtro é aplicado.
- [ ] Dado um nome desconhecido, então a API devolve 400 com o nome em falta.

### Notas técnicas
- Estende `search.v1.json` e o executor.

### Testes esperados
- Integração (PostgreSQL): semântica "todas", `includeMe`, erros.
