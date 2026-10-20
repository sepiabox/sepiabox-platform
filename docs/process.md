# Autonomous development cycle

How agents deliver the backlog without waiting for a person. Read `CLAUDE.md`, `docs/architecture.md` and `docs/decisions.md` first. When something is ambiguous, choose the simplest, most conservative option, record it in `docs/decisions.md` and carry on.

## Rules that are never broken

- No commits or pushes straight to `main`. One branch per story: `story/S-XXX-<slug>`.
- Never disable, delete or weaken tests, lint or workflows to make something pass.
- Never commit secrets. A story that needs a credential that does not exist becomes `blocked`.
- No `--force` on shared branches; `main`'s history is never rewritten.
- At most **6 development → review iterations** per story (raised from 4 by the owner on 2026-10-19). When the limit is reached the story becomes `blocked` with the full report, and work moves on to the next one.

## Step 0 — Pick the story

1. Update `main`.
2. Take the next `todo` story **of the current sprint** in `docs/backlog/README.md` whose dependencies are all `done`, in backlog order. The current sprint is the first one with a story not yet `done` or `blocked`; the next sprint starts only when it is finished, and there are no order overrides (D-054). Sprints after the MVP do not start while any MVP story is `blocked`: report it in the Agent Log and stop (D-054).
3. Set it to `doing` and create the branch.
4. If nothing is eligible but some stories are `blocked`, go to "End of the cycle". A `todo` story whose dependency is `blocked` is set to `blocked` too, with the reason "depends on S-XXX" (D-054).

**Manual checks.** A story whose only open acceptance criteria are manual owner checks (a real deploy, a browser or phone check) is `done` once its automated criteria pass; list the manual checks under "Owner acceptance" in `docs/operations.md`. A live run that the story itself requires (the golden questions of S-025/S-026) is not a manual check: without its credential the story is `blocked` (D-054).

## Step 1 — Develop or fix

1. First iteration: implement every acceptance criterion. Later iterations: fix **only** the open rows of the review table.
2. Every acceptance criterion and every fixed bug has a test (for a bug, a test that failed before the fix).
3. Run locally, in this order, until everything passes: build → format/lint → full test suite.
4. Small, descriptive commits: `S-XXX: <what>`.

## Step 2 — Parallel review (Atlas + Spock)

Write the diff first: `git diff main...HEAD > /tmp/S-XXX.diff`. Then start two subagents in parallel, each with its own isolated context.

**Atlas** reviews with full knowledge of the project:

> You are Atlas, a senior reviewer who knows the project, the business and the technology. Read `CLAUDE.md`, `docs/`, story S-XXX and the diff in /tmp/S-XXX.diff; you may browse the repository. Assess: each acceptance criterion, consistency with the architecture and recorded decisions, business rules and edge cases, test quality and coverage, impact on other parts of the system. Return only a list of findings as `priority | file:line | description | how to fix`. Do not change any file.

**Spock** reviews only the technique, with no project context:

> You are Spock, an expert in <STACK>. You do not know this project or its business, and you **must not read** `docs/`, `CLAUDE.md`, issues or other repository files: analyse **only** /tmp/S-XXX.diff. Assess strictly technically: correctness, bugs, security, concurrency, error handling, performance, readability, idiomatic use of the language and framework, fragile tests. Where the diff is unclear without context, that is itself a (readability) finding. Return only a list of findings as `priority | file:line | description | how to fix`. Do not change any file.

`<STACK>` per repository:

| Repository | Stack given to Spock |
|---|---|
| `sepiabox-platform` | .NET 10 / C#, ASP.NET Core and Blazor, EF Core with Npgsql, PostgreSQL (pgvector, PostGIS), Azure SDK for .NET, xUnit |
| `sepiabox-infra` | Azure Bicep, PowerShell 7, Bash, GitHub Actions |
| `sepiabox-site` | HTML, CSS, JavaScript, Playwright |

### Priorities

| Level | Meaning | Rule |
|---|---|---|
| **P0** | Breaks functionality, loses data, security hole | Must be fixed |
| **P1** | Real bug or unmet acceptance criterion | Must be fixed |
| **P2** | Relevant design or maintenance problem, missing test | Fix, or justify in writing |
| **P3** | Minor improvement, readability | Optional; if not fixed, open a follow-up issue |
| **P4** | Nit or preference | Optional; may be ignored |

### Consolidation

Merge both lists, remove duplicates (when both point at the same thing keep the higher priority and mark it `Atlas+Spock`), discard false positives with a one-line reason, and produce:

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P1 | Atlas | ... | ❌ No | ... |
| P3 | Spock | ... | ➡️ Follow-up #N | ... |

The table, for every iteration, is kept in `docs/reviews/S-XXX.md`.

## Step 3 — Decide

- Any P0/P1 open, or a P2 without justification → back to Step 1 (one more iteration).
- Otherwise → open follow-up issues for unresolved P3s and go to Step 4.

## Step 4 — Pull request and CI

1. Push the branch and open the PR: title `S-XXX: <title>`, the story file, a summary of the change, the decisions taken and the final review table.
2. Follow the workflows until they finish.
3. Classify any failure:
   - **Code failure** (tests, lint, build, breakage caused by the diff) → Step 1 on the same branch (one more iteration; then Step 2 again on the new diff only).
   - **Infrastructure failure** (runner unavailable, network timeout, rate limit, external service down, quota, missing CI secret) → report it (see Communication) and re-run the failed jobs **one hour later**. Meanwhile, move on to the next eligible story that does not depend on this one. After 3 failed attempts the story becomes `blocked`.
   - When unsure, re-run once; if it fails the same way, treat it as a code failure.

## Step 5 — Close

With every check green:

1. Squash-merge the PR and delete the branch.
2. Confirm the story is `done` in `docs/backlog/README.md` (the PR sets it).
3. Update `CHANGELOG.md` if it exists.

## Step 6 — Repeat

Back to Step 0, until no eligible `todo` story is left.

## Blocked story

When something really needs a person (a credential, a paid account, an irreversible business decision, the iteration limit): set the story to `blocked`, write the exact reason and what is needed in the Agent Log, leave the branch or PR as a draft, and **carry on with the next story of the same sprint**. A sprint whose remaining stories are all `blocked` is finished; the next sprint may start, except that no post-MVP sprint starts while an MVP story is `blocked` (D-054).

## Communication

The owner is contacted only for an infrastructure failure, a blocked story, or the end of the cycle. Channel: comments on the **Agent Log** issue in `sepiabox-platform`. Short messages: story, what happened, what was tried, the next attempt (time) or what is needed.

## End of the cycle

A final report in `docs/reports/<date>.md` and in the Agent Log: stories completed (with PRs), stories blocked (reason and what is needed), follow-ups opened, new decisions in `docs/decisions.md`, and any risks noticed.
