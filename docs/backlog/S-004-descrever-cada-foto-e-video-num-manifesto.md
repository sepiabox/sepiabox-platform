## S-004 — Descrever cada foto e vídeo num manifesto
**Épico:** Ingestão local
**Depende de:** S-002
**Prioridade:** Alta
**Tamanho:** M

### Contexto
O PC processa o HD e a nuvem só importa o resultado (D-005). O resultado é um manifesto JSON por asset, identificado pelo SHA-256 do conteúdo (D-014). Esta story cria o comando `process` com metadados básicos e retoma; miniaturas, vídeo, embeddings e rostos acrescentam campos em stories seguintes.

### Âmbito
- `sepiabox-ingest process <pasta> --out <workdir> [--source-name <nome>]` (nome da fonte por defeito = nome da pasta).
- Por foto/vídeo: SHA-256 (hex minúsculo), tamanho, content type, `capturedAt`, GPS, câmara (make/model), largura/altura.
- Escreve `<workdir>/manifests/<sha256>.json` e o JSON Schema `docs/schemas/manifest.v1.json`.
- Estado em `<workdir>/state.db` (SQLite: caminho absoluto, tamanho, mtime, sha256, estado); ficheiros inalterados não são relidos.
- Ficheiros idênticos em caminhos diferentes → um manifesto com várias `sources`.
- Formato v1:
```json
{
  "schemaVersion": 1,
  "id": "<sha256>",
  "kind": "photo",
  "sizeBytes": 2048,
  "contentType": "image/jpeg",
  "capturedAt": "2019-07-14T16:30:05",
  "location": { "latitude": 41.18, "longitude": -8.69 },
  "camera": { "make": "Apple", "model": "iPhone 8" },
  "width": 4032,
  "height": 3024,
  "durationSeconds": null,
  "sources": [ { "type": "local", "name": "HD da Ana", "path": "2019/praia.jpg", "modifiedAt": "2019-07-15T10:00:00Z" } ],
  "derivatives": [],
  "embeddings": {},
  "facesProcessed": false,
  "faces": []
}
```

### Fora de âmbito
- Miniaturas (S-005), vídeo (S-006), envio (S-007).

### Critérios de aceitação
- [ ] Dado uma foto com data, GPS e câmara no EXIF, quando corro `process`, então o manifesto tem `capturedAt`, `location` e `camera` corretos.
- [ ] Dado dois ficheiros idênticos em pastas diferentes, quando corro `process`, então existe um manifesto com 2 `sources`.
- [ ] Dado um `process` concluído, quando corro de novo sem alterações, então nenhum ficheiro é relido (contador de saltados = total).
- [ ] Dado um ficheiro cujo tamanho ou mtime mudou, quando corro de novo, então é reprocessado.
- [ ] Dado qualquer manifesto gerado, quando é validado contra `docs/schemas/manifest.v1.json`, então é válido.
- [ ] Dado ficheiros de origem só de leitura, quando corro `process`, então termina sem erros (nunca abre em escrita).

### Notas técnicas
- `Sepiabox.Pipeline/Media/MetadataReader.cs` (estende o uso de MetadataExtractor: `GpsDirectory.GetGeoLocation()`, `ExifIfd0Directory` make/model).
- `Sepiabox.Pipeline/Manifests/Manifest.cs` (records + `System.Text.Json`, camelCase).
- `Sepiabox.Ingest/Processing/ProcessCommand.cs`, `StateStore.cs` (Microsoft.Data.Sqlite).
- Fixture com GPS: criar em código de teste com Magick.NET (`ExifProfile.SetValue`) ou guardar um JPEG pequeno gerado em `tests/.../TestData`.
- Validação de schema nos testes: pacote `JsonSchema.Net`.
- Decisões: D-005, D-014.

### Testes esperados
- Unitários: leitura de metadados, serialização do manifesto, estado SQLite.
- Integração: `process` numa pasta temporária, retoma e reprocessamento.
