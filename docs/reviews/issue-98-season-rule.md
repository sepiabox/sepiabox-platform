# Review of issue #98 — Word the season rule by the season's end; a month in any year is a list

Process: [docs/process.md](../process.md). The question translator read "last summer" in October as the summer still to come in some runs. Mozart reworded the rule (D-113): "last <season>" is the season whose last day is the most recent before today, a season still in progress is never "last", winter ends on the last day of February (the 29th in a leap year), the southern hemisphere has its own seasons and time zones near the equator use the northern ones. Atlas and Spock reviewed it. The prompt and its live recording are reviewed together (D-090).

## Iteration 1

Diff: `13487ad...d6265bd`.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3 | Atlas | The end of winter in a leap year and time zones near the equator were not said | ✅ Yes (`d6265bd`) | Both sentences added; D-073 is refined by D-113 |

## Iteration 2

The live run at `d6265bd` failed one golden question of 132: `december-any-year/en` was translated as a count. Mozart added the rule that a month "in any year", "every year", "de qualquer ano" or "de todos os anos" is `months` alone, a list unless the question asks how many (`1b928e1`). The next live run passed 132 of 132 and was recorded (`65480b1`); the acceptance run left the file as it was, and the replays passed.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| — | Atlas | Re-checked the any-year rule and the recording | ✅ Approved | — |

## Iteration 3

Spock's light pass at `65480b1`: no P0 to P3. The replays pass (Search 141, Web 1,244), the format budget of 19 optional properties is untouched (its mutant is killed), and the recording changes one entry, `this-year-videos/en`, from `years` to an equivalent date range, which the golden comparison accepts.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P4 | Spock | Ten golden questions already appear word for word in the prompt's examples, from before this PR, so the live run does not test generalization for them | ⏭️ Follow-up | A separate issue rewords those examples |
| P4 | Spock | Replays do not guard the prompt's wording; only a live run catches a stale recording | ✅ No change needed | Known and recorded in D-113 |

## Iteration 4

`f6ea5be` merges main (D-107 to D-109 and D-119 kept with D-113 in number order). Mozart checked it against the findings and CI.
