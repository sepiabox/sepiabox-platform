## S-012 — Dar nome aos lugares com GeoNames
**Épico:** Catálogo na nuvem
**Depende de:** S-008
**Prioridade:** Alta
**Tamanho:** M

### Contexto
"Fotos de Lisboa" precisa de nomes de lugares. Usa-se GeoNames offline em vez de um serviço pago por consulta (D-009).

### Âmbito
- Tabela `geonames` (geoname_id, name, ascii_name, country_code, admin1_name, population, location geography) com índice GiST.
- `sepiabox-worker geonames load <cities500.zip|txt> --admin1 <admin1CodesASCII.txt> --countries <countryInfo.txt>`: carga idempotente (fonte: `https://download.geonames.org/export/dump/`).
- Na importação: cidade mais próxima num raio de 50 km → `place_city`, `place_region`, `place_country` no asset.
- `sepiabox-worker places refresh`: resolve assets sem lugar.
- Detalhe da foto mostra "Matosinhos, Porto, Portugal".
- Atribuição CC BY 4.0 no README.

### Fora de âmbito
- Lugares pessoais (S-014).

### Critérios de aceitação
- [ ] Dado um ficheiro de teste com 5 cidades, quando o carrego duas vezes, então a tabela tem 5 linhas.
- [ ] Dado um asset em 41.18, −8.69, quando é importado, então o lugar é "Matosinhos".
- [ ] Dado um asset a 300 km da cidade mais próxima, então fica sem lugar.
- [ ] Dado `places refresh`, então assets antigos sem lugar ficam resolvidos.

### Notas técnicas
- Consulta: `ORDER BY location <-> @point LIMIT 1` com `ST_DWithin(location, @point, 50000)`.
- Fixture TSV pequena em `tests/.../TestData/geonames-sample.txt`.
- Decisões: D-009.

### Testes esperados
- Integração (PostgreSQL): carga, resolução, raio, refresh.
