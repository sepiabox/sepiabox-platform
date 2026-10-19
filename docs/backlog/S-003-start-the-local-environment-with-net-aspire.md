## S-003 — Start the local environment with .NET Aspire
**Epic:** Foundation
**Depends on:** S-001
**Priority:** High
**Size:** M

### Context
The catalog uses PostgreSQL with pgvector and PostGIS, and files go to Blob Storage. To develop without spending on the cloud, a single command must start everything locally. The same AppHost will be used by `azd` to provision Azure (S-032).

### Scope
- `infra/postgres/Dockerfile`: `FROM postgis/postgis:17-3.5` + `apt-get install postgresql-17-pgvector`.
- `src/Sepiabox.AppHost` (Aspire) with: PostgreSQL via `AddAzurePostgresFlexibleServer("pg").RunAsContainer(c => c.WithDockerfile("../../infra/postgres").WithDataVolume())` and a `catalog` database; Azure Storage with `RunAsEmulator()` and `blobs`; the web project.
- `src/Sepiabox.ServiceDefaults` (Aspire default: health checks, OpenTelemetry).
- `src/Sepiabox.Web`: Blazor Web App (interactive server) with a home page "Sepiabox" and a `/health` endpoint that checks the database and blob storage.

### Out of scope
- Authentication (S-010), database schema (S-008).

### Acceptance criteria
- [ ] Given Docker is available, when the Aspire app starts in a test (`DistributedApplicationTestingBuilder`), then `GET /health` on the web returns 200.
- [ ] Given the AppHost's PostgreSQL, when I run `CREATE EXTENSION IF NOT EXISTS vector; CREATE EXTENSION IF NOT EXISTS postgis;`, then both are created.
- [ ] Given the web is running, when I request `/`, then the response is 200 and contains "Sepiabox".

### Technical notes
- Packages: `Aspire.Hosting.AppHost`, `Aspire.Hosting.Azure.PostgreSQL`, `Aspire.Hosting.Azure.Storage`, `Aspire.Npgsql`, `Aspire.Azure.Storage.Blobs`, `Aspire.Hosting.Testing`.
- New test project `tests/Sepiabox.AppHost.Tests` (integration).
- Decisions: D-002, D-003, D-012.

### Expected tests
- Integration (Category=Integration): AppHost startup, `/health`, PostgreSQL extensions.
