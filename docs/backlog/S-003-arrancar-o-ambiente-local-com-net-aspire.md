## S-003 — Arrancar o ambiente local com .NET Aspire
**Épico:** Fundação
**Depende de:** S-001
**Prioridade:** Alta
**Tamanho:** M

### Contexto
O catálogo usa PostgreSQL com pgvector e PostGIS e os ficheiros vão para Blob Storage. Para desenvolver sem gastar na nuvem, um único comando tem de arrancar tudo localmente. O mesmo AppHost será usado por `azd` para provisionar o Azure (S-032).

### Âmbito
- `infra/postgres/Dockerfile`: `FROM postgis/postgis:17-3.5` + `apt-get install postgresql-17-pgvector`.
- `src/Sepiabox.AppHost` (Aspire) com: PostgreSQL via `AddAzurePostgresFlexibleServer("pg").RunAsContainer(c => c.WithDockerfile("../../infra/postgres").WithDataVolume())` e base `catalog`; Azure Storage com `RunAsEmulator()` e blobs `blobs`; projeto web.
- `src/Sepiabox.ServiceDefaults` (padrão Aspire: health checks, OpenTelemetry).
- `src/Sepiabox.Web`: Blazor Web App (interactive server) com página inicial "Sepiabox" e endpoint `/health` que verifica a base de dados e o blob.

### Fora de âmbito
- Autenticação (S-010), esquema da base de dados (S-008).

### Critérios de aceitação
- [ ] Dado Docker disponível, quando a app Aspire arranca num teste (`DistributedApplicationTestingBuilder`), então `GET /health` na web devolve 200.
- [ ] Dado o PostgreSQL do AppHost, quando executo `CREATE EXTENSION IF NOT EXISTS vector; CREATE EXTENSION IF NOT EXISTS postgis;`, então ambas são criadas.
- [ ] Dado a web em execução, quando peço `/`, então a resposta é 200 e contém "Sepiabox".

### Notas técnicas
- Pacotes: `Aspire.Hosting.AppHost`, `Aspire.Hosting.Azure.PostgreSQL`, `Aspire.Hosting.Azure.Storage`, `Aspire.Npgsql`, `Aspire.Azure.Storage.Blobs`, `Aspire.Hosting.Testing`.
- Novo projeto de testes `tests/Sepiabox.AppHost.Tests` (integração).
- Decisões: D-002, D-003, D-012.

### Testes esperados
- Integração (Category=Integration): arranque do AppHost, `/health`, extensões no PostgreSQL.
