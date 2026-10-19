## S-024 — Classificar ocasiões por tipo
**Épico:** Ocasiões
**Depende de:** S-023, S-016
**Prioridade:** Média
**Tamanho:** M

### Contexto
Saber que uma ocasião é um casamento usa os embeddings CLIP já guardados (zero-shot), sem modelo novo (D-019).

### Âmbito
- `eventTypes.json`: casamento ("a photo of a wedding"), aniversário ("a photo of a birthday party"), praia ("a photo at the beach"), natal ("a photo of a Christmas celebration"), formatura ("a photo of a graduation ceremony"), neve ("a photo in the snow"), festa ("a photo of a party"). Sem categorias de religião ou saúde.
- Por foto: softmax (temperatura 100) entre os textos; tipo do evento = rótulo com maior número de fotos com probabilidade ≥ 0,35, se ≥ 30% das fotos; senão sem tipo.
- Regra extra "viagem": ocasião a mais de 100 km do lugar "Casa" (S-014, se existir) com duração ≥ 1 dia.
- DSL `eventTypes: ["casamento"]`; filtro na página de ocasiões.

### Fora de âmbito
- Contagens e respostas (S-026).

### Critérios de aceitação
- [ ] Dado embeddings sintéticos alinhados com "wedding" em 60% das fotos, então o tipo é casamento.
- [ ] Dado nenhum rótulo acima do limiar em 30% das fotos, então o tipo fica vazio.
- [ ] Dado uma ocasião de 2 dias a 300 km de Casa, então tem o tipo viagem.
- [ ] Dado `eventTypes=[casamento]`, então só vêm ocasiões desse tipo.

### Notas técnicas
- Embeddings de texto calculados uma vez (S-016) e guardados em memória; nos testes, codificador falso.
- Decisões: D-019.

### Testes esperados
- Unitários: voto, limiares, viagem. Integração: filtro.
