## S-014 — Guardar lugares pessoais e filtrar por eles
**Épico:** Pesquisa
**Depende de:** S-013
**Prioridade:** Média
**Tamanho:** S

### Contexto
"A casa da praia ao longo dos anos" e "fotos a mais de 100 km de casa" dependem de lugares que só a família conhece.

### Âmbito
- Tabela `saved_places` (id, name único sem distinção de maiúsculas, location, radius_m).
- Página `/lugares`: criar a partir da localização de uma foto ("Guardar este lugar") ou de lat/lon, editar raio, apagar.
- DSL: `savedPlaces: ["Casa da praia"]`, `distanceFrom: { "place": "Casa", "minKm": 100 }`.
- Vista "ao longo dos anos" para um lugar (agrupado por ano).

### Fora de âmbito
- Deteção automática de "casa".

### Critérios de aceitação
- [ ] Dado um lugar com raio 200 m, então o filtro devolve só assets dentro do raio.
- [ ] Dado `distanceFrom` Casa com `minKm=100`, então só vêm assets a mais de 100 km.
- [ ] Dado um lugar apagado, então os assets continuam intactos.
- [ ] Dado dois lugares com o mesmo nome em maiúsculas diferentes, então o segundo é recusado.

### Notas técnicas
- `ST_DWithin` em `geography`. Estende `search.v1.json`.

### Testes esperados
- Integração (PostgreSQL): raio, distância mínima, unicidade. bUnit: formulário.
