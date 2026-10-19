## S-002 — Ler um disco e relatar o que contém
**Épico:** Ingestão local
**Depende de:** S-001
**Prioridade:** Alta
**Tamanho:** M

### Contexto
Primeiro contacto com o HD da família: saber quantas fotos e vídeos existem, de que anos, quantos duplicados e se há ficheiros ilegíveis (sinal de disco a falhar), sem alterar nada. Implementado no PR #1 (`sepiabox-ingest scan`).

### Âmbito
- `sepiabox-ingest scan <pasta> [--report <ficheiro.json>] [--no-duplicates]`.
- Classificação por extensão (`MediaTypes`), data de captura (`CaptureDateReader`, MetadataExtractor), duplicados exatos (`DuplicateFinder`: tamanho → primeiros/últimos 64 KB → SHA-256 completo).
- Ignora pastas de sistema (`$RECYCLE.BIN`, `System Volume Information`, `.Trashes`, `@eaDir`…) e ficheiros `._`.
- Resumo na consola e relatório JSON.

### Fora de âmbito
- Gerar manifestos ou miniaturas (S-004, S-005).

### Critérios de aceitação
- [ ] Dado uma pasta com fotos, vídeos, outros ficheiros e uma pasta `$RECYCLE.BIN`, quando corro `scan`, então os totais por tipo excluem a reciclagem.
- [ ] Dado uma foto com `DateTimeOriginal` de 2019, quando corro `scan`, então ela conta no ano 2019.
- [ ] Dado duas cópias idênticas, quando corro `scan`, então há 1 grupo de duplicados com 1 cópia extra.
- [ ] Dado um ficheiro ilegível, quando corro `scan`, então ele aparece em "Problems" e a leitura continua.
- [ ] Dado `--report`, então o JSON contém todos os caminhos de duplicados.

### Notas técnicas
- Já implementado no PR #1 (`claude/ingest-scan`). Concluir: rever, garantir CI verde, fazer merge e marcar `done`.

### Testes esperados
- Unitários: classificação, leitura de datas (fixtures JPEG), duplicados, opções, relatório. Integração: varrimento de uma pasta temporária.
