## S-005 — Gerar miniaturas das fotos
**Épico:** Ingestão local
**Depende de:** S-004
**Prioridade:** Alta
**Tamanho:** S

### Contexto
A web mostra miniaturas e pré-visualizações, nunca os originais (que ficam em Cold). Geram-se no PC durante o `process` (D-015).

### Âmbito
- Estágio de miniaturas no `process` com Magick.NET (`Magick.NET-Q8-AnyCPU`): `thumb-256.webp` e `preview-1280.webp` (lado maior, proporção mantida, orientação EXIF aplicada, metadados removidos, qualidade 80) em `<workdir>/derivatives/<sha256>/`.
- Regista em `derivatives` do manifesto: `{ "name": "thumb-256", "path": "derivatives/<id>/thumb-256.webp", "width": 256, "height": 192, "contentType": "image/webp" }`.
- Imagens mais pequenas do que o alvo não são ampliadas.
- Falha numa imagem → problema no relatório, o processamento continua.

### Fora de âmbito
- Vídeo (S-006).

### Critérios de aceitação
- [ ] Dado um JPEG 4000×3000, quando corro `process`, então existem `thumb-256.webp` 256×192 e `preview-1280.webp` 1280×960.
- [ ] Dado um JPEG com `Orientation=6`, então a miniatura fica em retrato (altura > largura).
- [ ] Dado um `.jpg` corrompido, então o manifesto não tem derivados e o problema é reportado.
- [ ] Dado um segundo `process` sem alterações, então as miniaturas não são regeneradas.
- [ ] Dado uma foto com GPS, então a miniatura não contém EXIF.

### Notas técnicas
- `Sepiabox.Pipeline/Imaging/ThumbnailGenerator.cs`.
- Fixtures geradas nos testes com Magick.NET (cor sólida, orientação via `ExifProfile`).
- Decisões: D-015.

### Testes esperados
- Unitários: dimensões, orientação, remoção de metadados, imagem corrompida.
