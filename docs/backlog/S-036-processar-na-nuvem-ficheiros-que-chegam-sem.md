## S-036 — Processar na nuvem ficheiros que chegam sem manifesto
**Épico:** Fontes na nuvem
**Depende de:** S-033, S-006, S-015
**Prioridade:** Média
**Tamanho:** M

### Contexto
Ficheiros de fontes na nuvem (Drive, OneDrive) chegam sem o processamento do PC. O worker corre o mesmo pipeline (`Sepiabox.Pipeline`).

### Âmbito
- Contentor `incoming` (blob + metadados: tipo de fonte, nome, caminho, id externo, modo referência ou custódia).
- Segunda subscrição do Event Grid filtrada a `incoming` → fila `incoming` (`maxDeliveryCount` 5).
- `sepiabox-worker process --from-queue`: hash, metadados, miniaturas, vídeo, CLIP (e rostos se ativos); escreve derivados e manifesto como o PC; em custódia move o original para `originals` (Cold); em referência não guarda o original; apaga o blob de `incoming`.
- Imagem Docker do worker com FFmpeg e modelos (download com verificação SHA-256 no build).

### Fora de âmbito
- Ligar contas (S-037, S-039).

### Critérios de aceitação
- [ ] Dado um blob em `incoming` (Azurite), quando o worker processa, então existem manifesto e derivados e o blob de `incoming` desaparece.
- [ ] Dado modo referência, então nada é gravado em `originals`.
- [ ] Dado o mesmo ficheiro duas vezes, então o resultado é idempotente.
- [ ] Dado o Dockerfile do worker, então o build no CI produz uma imagem com `ffmpeg`.

### Notas técnicas
- Reutilizar os estágios de S-004/S-005/S-006/S-015.

### Testes esperados
- Integração (Azurite + PostgreSQL): processamento completo; build da imagem no CI.
