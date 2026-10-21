# Review of D-090 — Two implementers and two-track sprints

Process: [docs/process.md](../process.md). Docs only. The owner decided on 2026-10-21 to run two implementers, Tesla and Edison, in parallel and to make each sprint from 13 on twice as large, as two independent tracks, rather than borrowing from later sprints; the owner also allows moving from one sprint to the next without asking. Edison wrote the re-plan; the orchestrator (Mozart) applied the review fixes.

## Iteration 1

Diff: `ec867c4...6a48e6d`. Both reviewers checked the index by script: 64 stories, 16 sprints, no forward or cross-track dependency, every story file matching the index, critical path of 13.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3 | Atlas | S-041 (region search) reads previews through the storage code S-050 rewrites, but sat on the other track | ✅ Yes (`f917078`) | Moved to Sprint 14 Track A after S-051 |
| P3 | Atlas | S-052 (captions and albums) overlapped S-040 and S-057 in Sprint 15 | ✅ Yes (`f917078`) | Moved to Sprint 16 Track A; S-052 notes that the export and source deletion gain albums there |
| P3 | Atlas | No rules for the conflicts parallel branches always create | ✅ Yes (`f917078`) | process.md: branch from main, merge main after a merge, regenerate a branch-only migration instead of merging the snapshot, the orchestrator assigns D-numbers, keep both sides in the index and operations.md |
| P3 | Spock | CLAUDE.md's short workflow still described one implementer taking the first `todo` story | ✅ Yes (`f917078`) | Next story of your track, both tracks finish the sprint, drafts at iteration 1, DaVinci |
| P4 | Atlas | "No re-review when only P3/P4 remain" needed an exception for risky fixes | ✅ Yes (`f917078`) | Migrations, the manifest schema or merge, the translator's prompt or output format, and the golden set get a quick re-check |
| P4 | Atlas | No branching rule for "next story during review" | ✅ Yes (`f917078`) | Branch from main, never from a story in review |
| P4 | Spock | D-053 and D-054 still cite the old sprint numbers | ➖ Stays as is | Dated records; D-088 and D-090 explain the renumbering |

The owner also added a UI/UX reviewer, DaVinci, during the review; process.md and CLAUDE.md name the role (`f917078`).

## Iteration 2

Only P3/P4 findings remained, so per process.md the orchestrator checked the fixes against the diff and CI: the moves keep every dependency backward and no dependency crosses the two tracks of a sprint (Sprint 14: A = S-050, S-051, S-041; B = S-037, S-027. Sprint 15: A = S-036, S-057, S-040; B = S-045, S-046. Sprint 16: A = S-038, S-039, S-052; B = S-047, S-048, S-049).
