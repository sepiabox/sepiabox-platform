## S-018 — Detetar rostos no processamento
**Épico:** Pessoas
**Depende de:** S-005, S-015
**Prioridade:** Alta
**Tamanho:** M

### Contexto
"Fotos minhas com a Ana" exige reconhecer rostos. É opt-in (dado biométrico) e corre no PC (D-016).

### Âmbito
- `process --faces`. Modelos em `models.json`: YuNet `face_detection_yunet_2023mar.onnx` e SFace `face_recognition_sface_2021dec.onnx` (repositório `opencv/opencv_zoo`).
- Deteção na `preview-1280`: score ≥ 0,8, lado mínimo 40 px; alinhamento por 5 pontos para 112×112 (transformação de semelhança para os pontos de referência ArcFace); embedding SFace 128-d normalizado.
- Manifesto: `facesProcessed: true` e `faces: [{ "box": [x, y, w, h] (relativo 0..1), "score": 0.97, "embedding": "<base64>" }]`. Sem `--faces`: `facesProcessed: false`.

### Fora de âmbito
- Agrupar em pessoas (S-019).

### Critérios de aceitação
- [ ] Dado uma imagem de cor sólida, então 0 rostos.
- [ ] Dado a fixture com 1 rosto, então 1 rosto com score ≥ 0,8 e caixa dentro da imagem.
- [ ] Dado a mesma fixture redimensionada a 70%, então o cosseno entre os embeddings é ≥ 0,6.
- [ ] Dado `process` sem `--faces`, então `facesProcessed` é false e não há rostos.

### Notas técnicas
- Fixture: retrato de domínio público (ex.: retrato oficial do governo dos EUA no Wikimedia Commons), com fonte e licença em `tests/.../TestData/README.md`.
- Alinhamento com Magick.NET (`Distort` afim) ou matriz própria.
- Decisões: D-016.

### Testes esperados
- Unitários: transformação de alinhamento. Integração (modelos): deteção, embeddings, opt-in.
