## S-016 — Pesquisar fotos por descrição
**Épico:** Pesquisa
**Depende de:** S-013, S-015
**Prioridade:** Alta
**Tamanho:** M

### Contexto
O codificador de texto CLIP transforma "snow" num vetor comparável aos embeddings das fotos. A pesquisa é em inglês (D-017); o tradutor de perguntas (S-025) trata do português.

### Âmbito
- Modelo de texto `Xenova/clip-vit-base-patch32` (`onnx/text_model.onnx`, `vocab.json`, `merges.txt`) em `models.json`.
- Tokenizador CLIP BPE em C# (minúsculas, regex do CLIP, sufixo `</w>`, início 49406, fim 49407, comprimento 77).
- DSL: `visual: { "text": "snow", "minScore": 0.2 }`; ordena por semelhança, combinável com os outros filtros.
- UI: caixa "Descrever (em inglês)" na barra de pesquisa.

### Fora de âmbito
- Tradução de português (S-025).

### Critérios de aceitação
- [ ] Dado "a photo of a cat", quando tokenizo, então obtenho `[49406, 320, 1125, 539, 320, 2368, 49407]` seguido de padding.
- [ ] Dado um texto, então o embedding tem 512 valores e norma 1.
- [ ] Dado assets com embeddings sintéticos conhecidos, quando pesquiso um vetor próximo de um deles, então esse vem primeiro.
- [ ] Dado `visual` com `years=[2019]`, então só vêm assets de 2019, ordenados por semelhança.

### Notas técnicas
- Tentar `Microsoft.ML.Tokenizers` (BPE com sufixo de fim de palavra); se não reproduzir os ids de referência, implementar o BPE do CLIP (~150 linhas).
- `Sepiabox.Pipeline/Clip/ClipTextEncoder.cs`, `ClipTokenizer.cs`. Serviço singleton com carregamento preguiçoso na web.
- Decisões: D-016, D-017, D-020.

### Testes esperados
- Unitários: tokenizador com ids de referência. Integração: inferência de texto e consulta kNN.
