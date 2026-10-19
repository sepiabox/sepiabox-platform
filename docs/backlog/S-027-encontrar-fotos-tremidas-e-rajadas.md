## S-027 — Encontrar fotos tremidas e rajadas
**Épico:** Organização
**Depende de:** S-005, S-013
**Prioridade:** Baixa
**Tamanho:** M

### Contexto
HDs de família têm muitas fotos tremidas e sequências quase iguais. Ajudar a revê-las sem apagar nada.

### Âmbito
- No `process`: nitidez (variância do Laplaciano sobre a imagem em cinza com 512 px) e dHash de 64 bits; manifesto `quality: { "sharpness": 123.4, "dhash": "<hex>" }`; importação guarda.
- Página `/organizar`: rajadas (fotos com ≤ 2 s de diferença e distância de Hamming do dHash ≤ 10) e fotos tremidas (nitidez abaixo de um limiar configurável).
- Ação "Esconder" (flag `hidden`); assets escondidos ficam fora da pesquisa por defeito. DSL `quality: { "blurry": true }`, `includeHidden`.

### Fora de âmbito
- Apagar originais.

### Critérios de aceitação
- [ ] Dado uma imagem e a sua versão desfocada, então a desfocada tem nitidez menor.
- [ ] Dado uma imagem e a mesma redimensionada, então a distância do dHash é ≤ 4; para imagens diferentes é > 20.
- [ ] Dado 3 fotos com 1 s de diferença e dHash próximos, então formam uma rajada.
- [ ] Dado um asset escondido, então não aparece na pesquisa por defeito e aparece com `includeHidden`.

### Notas técnicas
- `Sepiabox.Pipeline/Imaging/QualityAnalyzer.cs` com Magick.NET.

### Testes esperados
- Unitários: nitidez, dHash, agrupamento. Integração: esconder e pesquisa.
