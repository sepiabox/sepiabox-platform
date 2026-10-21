# Review of D-082 — A faster CI integration job (issue #55)

Process: [docs/process.md](../process.md). Atlas knows the project; Spock sees only the diff. No story: the owner asked why the integration job took over 30 minutes.

## Root cause

The integration job took about 5 minutes until the S-056 merge and about 35 minutes since. The CI log shows `Sepiabox.AppHost.Tests` at 31 minutes for 6 tests; `WebImageTests` reads its child builds' output to the end, and idle MSBuild worker nodes (`/nodeReuse:true`) kept that pipe open for about 10 minutes per build. `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0` on the child processes fix it (over 30 minutes → about 50 s locally, no assertion changed).

## Iteration 1

Diff: `origin/main...11e91b7` (and `817bd48`, the CLAUDE.md line). Atlas checked that Deploy still only ships images that passed the test (it waits for the whole CI workflow on main, and `web-image` always runs there), the coverage trade-off and the billed minutes (about 45 → 16 per pull request). Spock checked that no assertion changed, the test filters (by running them), `HEAD^1` for pull request merge commits, that a skipped job stays green and the concurrency groups (pull requests cancel older runs; main never cancels).

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3 | Spock | Nothing bounds the wait if another process ever holds the pipe; jobs had the 6-hour default | ✅ Yes (iteration 2) | The test waits at most 1 minute for the output after exit and fails with a clear message; `timeout-minutes` on lint (10), integration (30) and web-image (30) |
| P3 | Atlas | Windows unit tests will bill 2× once the repository is private | — | Owner's decision, pending; the Windows job is unchanged |
| P4 | Atlas + Spock | The web-image path pattern missed some inputs | ✅ Yes (iteration 2) | Whole Ingest and Pipeline projects, the AppHost tests folder, `tests/Shared/`, `.gitignore` |
| P4 | Spock | The change check failed open when `git diff` errored | ✅ Yes (iteration 2) | The diff runs on its own; an error warns and runs the tests |
| P4 | Atlas | "(#55)" in a commit message does not close the issue | ✅ Yes | "Closes #55" in the pull request |
| P4 | Spock | Optional `-p:UseSharedCompilation=false` | — | Not done: the compiler server does not hold the pipe (checked); it would only slow the builds |

Decision: no findings open (the Windows question waits on the owner) → pull request.
