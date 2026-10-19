## S-007 — Enviar originais, derivados e manifestos para o armazenamento
**Épico:** Ingestão local
**Depende de:** S-003, S-004
**Prioridade:** Alta
**Tamanho:** M

### Contexto
Depois do `process`, o PC envia tudo para Blob Storage. O manifesto vai por último, porque a sua chegada dispara a importação (S-033). O envio de ~1,1 TB demora dias, por isso tem de retomar.

### Âmbito
- `sepiabox-ingest upload <workdir> --destination local|azure [--account <nome>]`.
- `local`: connection string `UseDevelopmentStorage=true` ou variável `SEPIABOX_BLOB_CONNECTION`. `azure`: `https://<conta>.blob.core.windows.net` com `DefaultAzureCredential` (login via `az login`).
- Contentores: `originals` (blob `<sha256>`, camada Cold só no destino azure), `derivatives` (`<sha256>/<nome>`), `manifests` (`<sha256>.json`, enviado só depois do original e dos derivados).
- Estado SQLite: o que já foi enviado; blobs existentes com o mesmo tamanho são saltados.
- Antes de enviar um original, confirma que o SHA-256 ainda coincide; se não, reporta e salta.
- 4 envios em paralelo, blocos de 8 MB, progresso em bytes.

### Fora de âmbito
- Importação no catálogo (S-008).

### Critérios de aceitação
- [ ] Dado um workdir processado e Azurite, quando corro `upload --destination local`, então os três contentores têm os blobs esperados.
- [ ] Dado o mesmo comando outra vez, então nada é reenviado.
- [ ] Dado um cancelamento a meio, quando corro de novo, então o resto é enviado e nada é duplicado.
- [ ] Dado um original alterado depois do `process`, então não é enviado e aparece como problema.
- [ ] Dado um asset, então o seu manifesto só é enviado depois do original e dos derivados (verificado pela ordem registada no estado).

### Notas técnicas
- Pacotes: `Azure.Storage.Blobs`, `Azure.Identity`; testes com `Testcontainers.Azurite`.
- Azurite pode não suportar a camada Cold: aplicar `AccessTier.Cold` só quando o destino é azure.
- Decisões: D-004, D-005.

### Testes esperados
- Integração (Azurite): envio, retoma, verificação de hash, ordem.
