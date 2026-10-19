## S-008 — Importar manifestos para o catálogo
**Épico:** Catálogo na nuvem
**Depende de:** S-003, S-007
**Prioridade:** Alta
**Tamanho:** M

### Contexto
O catálogo em PostgreSQL é a fonte de verdade da pesquisa. O worker lê manifestos do Blob Storage e grava-os de forma idempotente. Nesta story corre a pedido (`--once`); o disparo automático vem em S-033.

### Âmbito
- `src/Sepiabox.Catalog`: `CatalogDbContext` (EF Core + Npgsql + NetTopologySuite) e migração inicial com `assets` (id = sha256, kind, size_bytes, content_type, captured_at, location `geography(Point,4326)`, camera_make, camera_model, width, height, duration_seconds, imported_at), `asset_sources` (asset_id, type, name, path, modified_at; único por asset+type+name+path), `derivatives` (asset_id, name, blob_path, width, height, content_type), `manifest_imports` (blob_name, etag, imported_at).
- `src/Sepiabox.Worker` (`sepiabox-worker`): `migrate` e `import --once` (importa manifestos novos ou com etag diferente).
- Validação contra `manifest.v1.json`; manifesto inválido é registado e saltado.
- Biblioteca partilhada: sem coluna de dono (D-018).
- AppHost: adiciona o worker com referências à base e ao blob.

### Fora de âmbito
- Disparo por eventos (S-033), lugares (S-012), embeddings (S-015).

### Critérios de aceitação
- [ ] Dado um manifesto no Azurite, quando corro `import --once`, então existem o asset, as fontes e os derivados no PostgreSQL.
- [ ] Dado o mesmo manifesto importado duas vezes, então não há linhas duplicadas.
- [ ] Dado um manifesto do mesmo asset vindo de outra fonte, então é acrescentada uma fonte ao asset existente.
- [ ] Dado um manifesto inválido entre válidos, então os válidos são importados e o inválido é reportado.
- [ ] Dado uma base vazia, quando corro `migrate`, então todas as migrações aplicam-se.

### Notas técnicas
- PostgreSQL de teste com Testcontainers a partir de `infra/postgres/Dockerfile` (`ImageFromDockerfileBuilder`).
- Decisões: D-003, D-013, D-014, D-018.

### Testes esperados
- Integração (PostgreSQL + Azurite): importação, idempotência, fontes múltiplas, inválidos, migração.
