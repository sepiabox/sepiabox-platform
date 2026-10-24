# Review of issue #125 — A merge could delete the named person

Process: [docs/process.md](../process.md). The owner saw the merge page offer to delete a named person with about 1,500 faces and keep an unnamed one with about 100. The PR makes the server choose who stays (D-125) and makes the page show the danger. Atlas, DaVinci (the page is UI) and Spock reviewed it.

## Iteration 1

Diff: `main...fe655f0`.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| — | Atlas | Approved: the rule lives in `PeopleCuration.MergeAsync`, so a stale page or a hand-made POST cannot reverse it | ✅ No change needed | — |
| P2 | DaVinci | The "Will be deleted" and "Stays" badges reused the global `.badge` class and were pulled out of their cards | ✅ Yes | `.merge-badge`, static, inside each card |
| P2 | DaVinci | On phones the two cards and the arrow did not show the direction | ✅ Yes | Cards stacked, leaving first, arrow rotated to point down |
| P3 | DaVinci | "has a name (or is you)" read ambiguously | ✅ Yes | One warning per case: the person has a name, or is linked to the user |

## Iteration 2

Diff: `fe655f0...647803e`. DaVinci re-checked the page in EN and pt-BR at 390 and 1280 px and approved it. Spock reviewed the whole PR: no request can delete a named or user-linked person without an explicit swap. Every tier of `KeeperOf`, the swap guard, the hidden `Swap` field and the wording were mutated and the tests caught each mutant.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3 | Spock | Taking only the staying person's user link (not `?? other.UserEmail`) passed: it matters only on a swap that merges the linked person away | ✅ Yes | `Moves_the_user_link_to_the_person_that_stays_when_a_swap_merges_the_linked_one_away`, which fails under that mutant |
| P4 | Spock | The redirect after a merge to the page's own person (who may be the one that went) passed | ✅ No change needed | The page redirects to the person who stayed; noted here |

## Iteration 3

Mozart checked the fix against the finding and CI.
