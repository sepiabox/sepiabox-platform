## S-028 — Ver o espaço e as fontes do acervo
**Épico:** Organização
**Depende de:** S-026
**Prioridade:** Baixa
**Tamanho:** S

### Contexto
"Quanto espaço ocupam os vídeos de 2019?" e "que fotos só existem numa fonte?" ajudam a decidir o que guardar.

### Âmbito
- DSL: `aggregate.sum: "bytes"`; filtro `sourceCount: { "eq": 1 }` ou `{ "gte": 2 }`.
- Página `/acervo`: totais por tipo e ano (quantidade e tamanho), assets só numa fonte, assets em várias fontes (com nomes), 20 maiores vídeos.

### Fora de âmbito
- Recomendações automáticas de limpeza.

### Critérios de aceitação
- [ ] Dado assets semeados, então os totais por tipo e ano batem certo.
- [ ] Dado assets com 1 e 2 fontes, então `sourceCount.eq=1` devolve só os primeiros.
- [ ] Dado `aggregate.sum=bytes` com `kinds=[video]` e `years=[2019]`, então devolve a soma correta.

### Notas técnicas
- Reutiliza o executor de agregações de S-026.

### Testes esperados
- Integração (PostgreSQL): totais e filtros. bUnit: página.
