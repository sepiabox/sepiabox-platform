## S-041 — Encontrar fotos parecidas com uma região escolhida
**Épico:** Pesquisa
**Depende de:** S-017, S-023
**Prioridade:** Baixa
**Tamanho:** S

### Contexto
"Em que ocasiões usei este vestido vermelho?": o utilizador marca o vestido numa foto e o Sepiabox procura fotos parecidas, agrupadas por ocasião.

### Âmbito
- No detalhe: desenhar um retângulo na pré-visualização.
- Servidor recorta a `preview-1280` (Magick.NET), calcula o embedding CLIP do recorte e faz kNN.
- DSL: `similarToRegion: { "assetId": "<sha256>", "box": [x, y, w, h] }` (relativo 0..1); resultados agrupados por ocasião.

### Fora de âmbito
- Deteção automática de roupa.

### Critérios de aceitação
- [ ] Dado uma caixa relativa, então o recorte tem as dimensões esperadas em píxeis.
- [ ] Dado uma caixa fora de 0..1 ou com área zero, então a API devolve 400.
- [ ] Dado resultados, então vêm agrupados por ocasião.

### Notas técnicas
- Reutiliza o codificador de imagem de S-015 na web.

### Testes esperados
- Unitários: recorte e validação. Integração: agrupamento com embeddings sintéticos.
