## S-001 — Set up the repository foundation
**Epic:** Foundation
**Depends on:** none
**Priority:** High
**Size:** M

### Context
`sepiabox-platform` is the .NET core of Sepiabox and will be developed by autonomous agents. They need standard build, test and lint commands, a CI that blocks regressions and written conventions. Part of this already exists in PR sepiabox/sepiabox-platform#1 (solution `Sepiabox.slnx`, projects `Sepiabox.Pipeline`, `Sepiabox.Ingest`, xUnit tests and CI with build + tests on Linux and Windows).

### Scope
- Keep the `src/` and `tests/` structure and the `Sepiabox.slnx` solution.
- `.editorconfig` with C# style: file-scoped namespaces, mandatory braces, `var` when the type is evident, PascalCase/camelCase names, LF line endings, 4-space indentation.
- `Directory.Build.props`: `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild=true`, `TreatWarningsAsErrors=true` (part of this already exists).
- CI `.github/workflows/ci.yml`: `lint` job (`dotnet format Sepiabox.slnx --verify-no-changes`); `test` job on `ubuntu-latest` and `windows-latest` with `--filter "Category!=Integration"`; `integration` job only on `ubuntu-latest` with `--filter "Category=Integration"` (installs `ffmpeg` with apt).
- `README.md`: what it is, requirements (.NET 10 SDK, Docker), how to build, test and run `scan`.
- `CLAUDE.md` with: Stack; Commands (build, test, integration test, format, run the AppHost once it exists); Folder structure; Code conventions (docs, code and comments in English, UI text in Portuguese; comments only for the why; no secrets; the user's source files are never opened for writing); Tests (xUnit, names `Does_something_when_condition`, `[Trait("Category","Integration")]`); Backlog workflow (one story per PR, branch `story/S-XXX-slug`, update the status in `docs/backlog/README.md`, record new decisions in `docs/decisions.md`).
- Link `docs/architecture.md`, `docs/decisions.md`, `docs/operations.md` and `docs/backlog/README.md` from the README and from `CLAUDE.md`.
- License: keep `LICENSE` (AGPL-3.0) and declare `<PackageLicenseExpression>AGPL-3.0-only</PackageLicenseExpression>` in `Directory.Build.props`; `CLAUDE.md` states that new dependencies must have an AGPL-compatible license (MIT, Apache-2.0, BSD) and that the name and logo are not covered.

### Out of scope
- Any new product feature.
- Deployment.

### Acceptance criteria
- [ ] Given a clean clone, when I run `dotnet build Sepiabox.slnx`, then it builds with no warnings.
- [ ] Given a clean clone, when I run `dotnet format Sepiabox.slnx --verify-no-changes`, then it exits with code 0.
- [ ] Given a PR with a badly formatted C# file, when CI runs, then the `lint` job fails.
- [ ] Given the CI, when it runs on a PR, then tests not marked Integration pass on Linux and Windows and integration tests run only on Linux.
- [ ] Given `CLAUDE.md`, when it is read, then it contains the sections Stack, Commands, Structure, Code conventions, Tests and Backlog workflow.

### Technical notes
- Files: `.editorconfig`, `Directory.Build.props`, `.github/workflows/ci.yml`, `README.md`, `CLAUDE.md`.
- If PR sepiabox/sepiabox-platform#1 is not yet on `main`, merge it first.
- Decisions: D-001, D-010, D-011, D-012.

### Expected tests
- The CI itself (lint, tests, integration) is the check. The existing suite must stay green.
