## S-015 — Calcular embeddings CLIP no processamento
**Épico:** Pesquisa
**Depende de:** S-005, S-008
**Prioridade:** Alta
**Tamanho:** M

### Contexto
"Fotos na neve" e "parecidas com esta" usam embeddings CLIP (D-016), calculados uma vez no PC e guardados no pgvector.

### Âmbito
- `models/models.json` (nome, URL, SHA-256, licença) e `sepiabox-ingest models download` para `~/.sepiabox/models` (Windows: `%LOCALAPPDATA%\Sepiabox\models`); recusa ficheiros com SHA-256 diferente.
- CLIP ViT-B/32 visão: `https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/vision_model.onnx` (preencher o SHA-256 ao implementar).
- Pré-processamento: redimensionar lado menor para 224 (bicúbico), recorte central 224×224, normalização CLIP (média 0.48145466, 0.4578275, 0.40821073; desvio 0.26862954, 0.26130258, 0.27577711).
- Embedding 512-d normalizado L2; no manifesto `embeddings["clip-vit-b32"]` em base64 (float32 little-endian). Vídeos usam o `poster`.
- Importação: tabela `asset_embeddings` (asset_id, model, embedding `vector(512)`), índice HNSW com `vector_cosine_ops`.
- ONNX Runtime em CPU; `--gpu` usa CUDA se disponível.

### Fora de âmbito
- Codificador de texto (S-016).

### Critérios de aceitação
- [ ] Dado uma imagem, então o embedding tem 512 valores e norma 1 ± 0,001.
- [ ] Dado a mesma imagem duas vezes, então o cosseno é > 0,999.
- [ ] Dado uma imagem e a sua versão redimensionada, então o cosseno é > 0,9.
- [ ] Dado um ficheiro de modelo com SHA-256 errado, então o download falha com mensagem clara.
- [ ] Dado um manifesto com embedding, quando é importado, então existe a linha em `asset_embeddings`.

### Notas técnicas
- Pacotes: `Microsoft.ML.OnnxRuntime`, `Pgvector.EntityFrameworkCore`.
- Testes com modelo: Integration; cache do modelo no CI com `actions/cache`.
- Decisões: D-016.

### Testes esperados
- Unitários: pré-processamento (tensor e normalização), base64. Integração: inferência real e importação.
