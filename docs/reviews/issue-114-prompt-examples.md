# Review of issue #114 — The translator prompt's examples are not the golden questions

Process: [docs/process.md](../process.md). Spock found, while reviewing #98, that ten golden questions appeared word for word among the prompt's examples, so the live golden run partly measured copying. Edison reworded the examples and added `PromptExamplesTests` (D-122); Mozart merged main (with #98's season rule) and recorded the live run. Atlas and Spock reviewed it; the prompt and its recording are reviewed together (D-090).

## Iteration 1

Diff: `main...f7b7947`.

The first live run after the rewording passed 131 of 132: `lisbon-photos/pt` ("fotos de Lisboa") was given a visual description of the city. Mozart named the Portuguese place-only case in the visual rule ("fotos de Coimbra" has no visual) and added that example; the second run passed 132 of 132 and was recorded, the live acceptance run left the file as it was, and the replays passed.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3 | Atlas | `PromptExamplesTests` catches only an exact match after normalizing; a minimal rewording of a golden question would pass | ✅ No change needed | That is the aim: no golden question shown with its answer word for word |
| P4 | Atlas | D-122 said "eight examples" and listed nine changes | ✅ Yes | D-122 counts the eight copies, the near copy and the added example |
| P4 | Spock | The normalization keeps accents, so "verao" for "verão" would pass | ✅ No change needed | Unlikely to matter; noted here |

Spock's mutants: putting an English or a Portuguese golden question back, or one with other case, punctuation or spacing, fails the test; a broken example pattern fails the count guard. The two recording changes (`last-month-photos/pt`, `march-to-may-2021/pt`) are equivalent on the fixture catalog.

## Iteration 2

Mozart checked the fixes against the findings and CI.
