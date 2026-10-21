# Review of D-088 — Index-only mode in the backlog (S-063, S-064)

Process: [docs/process.md](../process.md). Docs only: Atlas checked the design against the decisions, Spock checked the files for consistency by script. The owner decided the design on 2026-10-21: index-only for any source, larger previews (2048 px photos, 1080p video), no Google Photos Picker, the mode placed next (Sprint 13), and acknowledged that index-only keeps no copy of the originals.

## Iteration 1

Diff: `c3d69af...49508a4`.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P2 | Atlas | The D-088 row spanned four lines and broke the decisions table | ✅ Yes (iteration 2) | One line, storage figures inline |
| P3 | Atlas | S-063 depended on S-040 and S-051 without needing them, which pushed it to Sprint 19 | ✅ Yes (iteration 2) | Owner chose Sprint 13; S-040 now depends on S-063; S-038/S-039 depend on S-064 |
| P3 | Atlas | Older ingest and worker builds refuse the new manifest fields | ✅ Yes (iteration 2) | Update note in operations.md; worker import criterion |
| P3 | Atlas + Spock | A work folder from before S-063 had no defined mode | ✅ Yes (iteration 2) | Counts as keeping originals; `--index-only` refused; criterion |
| P3 | Atlas | Nothing said index-only keeps no copy of the originals | ✅ Yes (iteration 2) | D-088 (1), a start-up warning, operations.md |
| P3 | Spock | operations.md said originals always go to the Cold tier | ✅ Yes (iteration 2) | Index-only exception |
| P3 | Spock | S-064's scope and criterion 7 disagreed on source links | ✅ Yes (iteration 2) | Links shown whether or not the original is stored |
| P3 | Spock | S-064 cited a pt-BR glossary that does not exist | ✅ Yes (iteration 2) | D-058's resource tests |
| P4 | Atlas + Spock | Embeddings and faces from different previews in the two modes | ✅ Yes (iteration 2) | Recorded as accepted |
| P4 | Atlas | "The player plays it" is not testable | ✅ Yes (iteration 2) | The player's source is `photos/{id}/proxy-1080p` |
| P4 | Spock | `--export` combination untestable in S-063 | ✅ Yes (iteration 2) | Tested in S-046 to S-049 |
| P4 | Spock | Exports need a real sample before their stories can finish | ✅ Yes (iteration 2) | Prerequisite and `blocked` reason in operations.md |
| P4 | Atlas | D-088's numbering note was stale | ✅ Yes (iteration 2) | Updated |

## Iteration 2

Diff: `49508a4...5f57f0b` (with a merge of main). Both approved; totals, dependencies and the 13-story critical path checked by script.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P4 | Spock | S-051 keeps index-only behaviour but did not depend on S-063 | ✅ Yes (`d6faaa8`) | Added |
| P4 | Atlas | The index still listed MVP stories as left | ✅ Yes (this commit) | "The MVP is done (all 36 stories)" |
| P4 | Spock | D-054 still names Sprint 15 for the portability refactor | ➖ Stays as is | A dated record; D-088 explains the shift |
