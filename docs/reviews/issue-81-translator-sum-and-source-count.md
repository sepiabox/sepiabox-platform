# Review of issue #81 — The question translator writes aggregate.sum and filters.sourceCount

Process: [docs/process.md](../process.md). Tesla implemented it (D-103, S-028's follow-up), Mozart made the live recordings (only the orchestrator runs `Category=LiveApi`), and Atlas and Spock reviewed it. The output format was probed with a real request before the first recording, since the API had refused a format that passed our own counters once (D-094).

## Iteration 1

Diff: `main...c1e923a`, with the first live recording (132/132, one after a retry).

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3 | Atlas | "duplicado" was a trigger for `sourceCount` ≥ 2, but the same photo twice is often on one drive | ✅ Yes (`859cd88`) | "entre origens" replaces it; "duplicate"/"duplicada" alone is no filter |
| P3 | Atlas | The format's remaining budget was nowhere written down | ✅ Yes (`859cd88`) | D-103 (5): 19 of 19 optional properties, 14 of 16 union-typed ones, and what to do at 16 |
| P4 | Atlas | A more complex format compiles more slowly on its first request | ✅ Yes (`859cd88`) | D-103 (6), covered by D-101's deadline and warm-up |

## Iteration 2

The prompt changed, so Mozart recorded again after merging main (S-041). Two runs stopped at 129/132, each on other questions: "photos from December in any year" was counted, "how many photos and how many videos" took a kinds filter, a source-count question lost its count, and "last summer" in October took the year before. The prompt gained a month-in-every-year example, a pt source-count example, "no kinds filter" for a count by kind, and a season can be in today's year (`7c0feb4`, `dfd759e`); none of the new examples is a golden question. The third run passed 132/132 (`b524c44`), the acceptance live run left its file unchanged and the CI replays passed.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3 | Atlas | The season rule is worded for summer; winter and the southern hemisphere read it less clearly | ❌ No | Issue #98: word it by the season's end, with a new recording |
| P4 | Atlas | D-103 (2) still named "duplicado" and did not record the tuning | ✅ Yes (`e63cca8`) | Updated |

## Iteration 3

Spock ran 16 mutations on the format, `WithoutNulls`, validation and the golden fixture; 4 survived, 1 of them equivalent.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3 | Spock | The fixture's sizes affected no check: every asset at 1000 bytes, or no per-group bytes, still passed (G3, G5, G6) | ✅ Yes (`48d0cdb`) | A test asserts the bytes the fixture gives the right items, in total and per group; the recording and the prompt are untouched |
| P4 | Spock | The prompt's wording is checked only by the live recording | ❌ No | Accepted: that is what the golden run is for |

## Iteration 4

Spock re-ran G1, G3–G6 on `48d0cdb`: all killed, no findings. Mozart merged main (S-045, S-046, S-051, #94; only `docs/decisions.md` conflicted) and checked that it touches neither the prompt, the search schema nor `QuestionService`, so the recording stays valid.
