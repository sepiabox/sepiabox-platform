## S-023 — Agrupar fotos em ocasiões
**Épico:** Ocasiões
**Depende de:** S-012
**Prioridade:** Média
**Tamanho:** M

### Contexto
"Quantos casamentos fomos" conta ocasiões, não fotos. Uma ocasião é uma sequência de fotos próximas no tempo e no espaço (D-019).

### Âmbito
- Tabela `events` (id, starts_at, ends_at, centroid, place_label, asset_count, type nulo, title) e `event_assets`.
- `sepiabox-worker events rebuild`: ordena assets com data; novo evento quando o intervalo > 6 h ou a distância ao asset geolocalizado anterior > 50 km; assets sem data ficam fora.
- Título: "<lugar> · <datas>" (ex.: "Lisboa · 14–16 dez 2024").
- Página `/ocasioes` (cartões com 3 miniaturas, datas, lugar, contagem) e detalhe; DSL `eventIds`.

### Fora de âmbito
- Tipo da ocasião (S-024).

### Critérios de aceitação
- [ ] Dado fotos com intervalo de 7 h, então ficam em ocasiões diferentes.
- [ ] Dado fotos 1 h e 60 km de distância, então ficam em ocasiões diferentes.
- [ ] Dado `rebuild` duas vezes, então o resultado é idêntico.
- [ ] Dado assets sem data, então não pertencem a nenhuma ocasião.
- [ ] Dado uma ocasião em Lisboa de 14 a 16/12/2024, então o título é "Lisboa · 14–16 dez 2024".

### Notas técnicas
- `Sepiabox.Catalog/Events/EventBuilder.cs` (lógica pura testável), persistência separada.
- Decisões: D-019.

### Testes esperados
- Unitários: regras de corte e títulos. Integração: rebuild idempotente.
