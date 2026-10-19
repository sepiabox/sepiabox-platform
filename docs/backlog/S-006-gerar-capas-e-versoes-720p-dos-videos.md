## S-006 — Gerar capas e versões 720p dos vídeos
**Épico:** Ingestão local
**Depende de:** S-004
**Prioridade:** Alta
**Tamanho:** M

### Contexto
Vídeos originais são grandes e ficam em Cold; a web mostra uma capa e toca uma versão 720p (D-015). O notebook de ingestão tem GPU NVIDIA, por isso o codificador de hardware é opcional.

### Âmbito
- Estágio de vídeo no `process` usando FFmpeg externo (`ffmpeg`/`ffprobe` no PATH ou `--ffmpeg <pasta>`).
- Duração via `ffprobe`; `poster.webp` (frame a 10% da duração, lado maior 1280) e `thumb-256.webp`; `proxy-720p.mp4` (H.264 CRF 28, preset veryfast, AAC 96k, `+faststart`, sem ampliar vídeos menores).
- `--hw nvenc` usa `h264_nvenc`.
- Manifesto: `durationSeconds` e derivados.
- Sem FFmpeg: aviso claro, vídeos marcados como pendentes no estado, fotos processadas normalmente.

### Fora de âmbito
- Transcrição de áudio.

### Critérios de aceitação
- [ ] Dado um vídeo 1920×1080 de 3 s (gerado com `ffmpeg -f lavfi -i testsrc`), quando corro `process`, então `proxy-720p.mp4` é 1280×720 com ~3 s e existe `poster.webp`.
- [ ] Dado um vídeo 640×360, então o proxy mantém 640×360.
- [ ] Dado FFmpeg ausente, então o `process` termina com código 0, aviso, e vídeos pendentes no estado.
- [ ] Dado um segundo `process`, então os vídeos não são reconvertidos.

### Notas técnicas
- `Sepiabox.Pipeline/Video/VideoProcessor.cs` com `System.Diagnostics.Process`; argumentos montados como lista (sem concatenar strings de shell).
- Testes com FFmpeg: `[Trait("Category","Integration")]` (o job de integração instala `ffmpeg`).
- Decisões: D-015.

### Testes esperados
- Unitários: construção de argumentos FFmpeg, cálculo de escala.
- Integração: conversão real de um vídeo gerado.
