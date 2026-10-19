## S-001 — Preparar a fundação do repositório
**Épico:** Fundação
**Depende de:** nenhuma
**Prioridade:** Alta
**Tamanho:** M

### Contexto
O `sepiabox-platform` é o núcleo .NET do Sepiabox e vai ser desenvolvido por agentes autónomos. Precisam de comandos de build, teste e lint padronizados, de um CI que bloqueie regressões e de convenções escritas. Parte já existe no PR #1 (solução `Sepiabox.slnx`, projetos `Sepiabox.Pipeline`, `Sepiabox.Ingest`, testes xUnit e CI com build + testes em Linux e Windows).

### Âmbito
- Manter a estrutura `src/` e `tests/` e a solução `Sepiabox.slnx`.
- `.editorconfig` com estilo C#: namespaces file-scoped, chavetas obrigatórias, `var` quando o tipo é evidente, nomes PascalCase/camelCase, finais de linha LF, indentação 4 espaços.
- `Directory.Build.props`: `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild=true`, `TreatWarningsAsErrors=true` (já existe parte).
- CI `.github/workflows/ci.yml`: job `lint` (`dotnet format Sepiabox.slnx --verify-no-changes`); job `test` em `ubuntu-latest` e `windows-latest` com `--filter "Category!=Integration"`; job `integration` só em `ubuntu-latest` com `--filter "Category=Integration"` (instala `ffmpeg` com apt).
- `README.md`: o que é, requisitos (.NET 10 SDK, Docker), como compilar, testar e correr o `scan`.
- `CLAUDE.md` com: Stack; Comandos (build, test, test de integração, format, run do AppHost quando existir); Estrutura de pastas; Convenções de código (inglês, comentários só para o porquê, sem segredos, ficheiros de origem do utilizador nunca abertos em escrita); Testes (xUnit, nomes `Does_something_when_condition`, `[Trait("Category","Integration")]`); Fluxo do backlog (uma story por PR, branch `story/S-XXX-slug`, atualizar o estado em `docs/backlog/README.md`, registar novas decisões em `docs/decisions.md`).
- Ligar `docs/architecture.md`, `docs/decisions.md`, `docs/operations.md` e `docs/backlog/README.md` a partir do README e do `CLAUDE.md`.
- Licença: manter `LICENSE` (AGPL-3.0) e declarar `<PackageLicenseExpression>AGPL-3.0-only</PackageLicenseExpression>` no `Directory.Build.props`; o `CLAUDE.md` diz que dependências novas têm de ter licença compatível com a AGPL (MIT, Apache-2.0, BSD) e que o nome e o logo não são abrangidos.

### Fora de âmbito
- Qualquer funcionalidade de produto nova.
- Deploy.

### Critérios de aceitação
- [ ] Dado um clone limpo, quando corro `dotnet build Sepiabox.slnx`, então compila sem avisos.
- [ ] Dado um clone limpo, quando corro `dotnet format Sepiabox.slnx --verify-no-changes`, então termina com código 0.
- [ ] Dado um PR com um ficheiro C# mal formatado, quando o CI corre, então o job `lint` falha.
- [ ] Dado o CI, quando corre num PR, então os testes não marcados como Integration passam em Linux e Windows e os de integração correm só em Linux.
- [ ] Dado o `CLAUDE.md`, quando é lido, então contém as secções Stack, Comandos, Estrutura, Convenções de código, Testes e Fluxo do backlog.

### Notas técnicas
- Ficheiros: `.editorconfig`, `Directory.Build.props`, `.github/workflows/ci.yml`, `README.md`, `CLAUDE.md`.
- Se o PR #1 ainda não estiver no `main`, fazer merge dele primeiro.
- Decisões: D-001, D-010, D-011, D-012.

### Testes esperados
- O próprio CI (lint, testes, integração) é a verificação. A suite existente tem de continuar verde.
