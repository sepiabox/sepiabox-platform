## S-017 — Mostrar mais fotos parecidas com esta
**Épico:** Pesquisa
**Depende de:** S-015, S-013
**Prioridade:** Média
**Tamanho:** S

### Contexto
"Mais fotos parecidas com esta" usa o embedding já guardado da própria foto, sem recalcular nada.

### Âmbito
- DSL: `similarTo: { "assetId": "<sha256>", "limit": 50 }`; exclui o próprio asset; combinável com filtros.
- Botão "Parecidas" no detalhe da foto → grelha de resultados.

### Fora de âmbito
- Semelhança por região da foto (S-041).

### Critérios de aceitação
- [ ] Dado embeddings sintéticos, então os resultados vêm por ordem de semelhança e não incluem a própria foto.
- [ ] Dado um asset sem embedding, então a UI mostra "Esta foto ainda não foi analisada".
- [ ] Dado `similarTo` com `years=[2020]`, então só vêm assets de 2020.

### Notas técnicas
- Operador `<=>` do pgvector.

### Testes esperados
- Integração (PostgreSQL): ordem, exclusão, filtros. bUnit: mensagem sem embedding.
