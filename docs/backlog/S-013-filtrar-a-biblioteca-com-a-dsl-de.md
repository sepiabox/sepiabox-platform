## S-013 — Filtrar a biblioteca com a DSL de pesquisa v1
**Épico:** Pesquisa
**Depende de:** S-012
**Prioridade:** Alta
**Tamanho:** M

### Contexto
Toda a pesquisa passa por uma DSL JSON única (D-020), usada pela UI de filtros e, mais tarde, pelo tradutor de perguntas. Esta story cria a DSL com filtros de tempo, tipo, lugar e fonte.

### Âmbito
- Records C# e `docs/schemas/search.v1.json`:
```json
{
  "filters": {
    "kinds": ["photo", "video"],
    "dateFrom": "2019-01-01", "dateTo": "2019-12-31",
    "years": [2019], "months": [12],
    "onThisDay": { "yearsAgo": 5 },
    "places": { "cities": ["Lisboa"], "countries": ["PT"] },
    "sources": ["HD da Ana"],
    "undatedOnly": false
  },
  "sort": "capturedAtDesc",
  "limit": 200
}
```
- `SearchService.Execute(SearchQuery)` (EF Core), `POST /api/search` (autenticado).
- Barra de filtros em `/` (ano, mês, tipo, cidade, fonte) e atalho "Neste dia"; filtros na URL (`?q=<json base64url>`).
- DSL inválida → 400 com mensagem.

### Fora de âmbito
- Pessoas, lugares pessoais, visual, ocasiões (stories seguintes estendem a DSL).

### Critérios de aceitação
- [ ] Dado assets de Lisboa e Porto em vários meses, quando filtro `cities=[Lisboa], months=[12]`, então só vêm os de Lisboa em dezembro.
- [ ] Dado um relógio fixo em 2026-10-19 e `onThisDay.yearsAgo=5`, então só vêm assets de 19/10/2021.
- [ ] Dado vários filtros, então combinam-se com E.
- [ ] Dado uma DSL com campo desconhecido, então a API devolve 400.
- [ ] Dado filtros escolhidos na UI, quando copio o URL e o abro, então os mesmos filtros são aplicados.

### Notas técnicas
- `Sepiabox.Catalog/Search/SearchQuery.cs`, `SearchService.cs`; `TimeProvider` para o relógio.
- Decisões: D-020.

### Testes esperados
- Unitários: validação da DSL, ida e volta da URL. Integração (PostgreSQL): cada filtro e combinações.
