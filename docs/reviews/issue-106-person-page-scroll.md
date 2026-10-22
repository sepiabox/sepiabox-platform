# Review of issue #106 — Keep the place on the person page after a face correction

Process: [docs/process.md](../process.md). The owner reported that "not this person" on the person page reloaded it at the top of a long list. Edison fixed it: each face card has an `id`, and the redirect after a correction (not this person, not a face, give back, move) carries a fragment to the card that followed the corrected one, else the one before it. Atlas, DaVinci and Spock reviewed it. The PR was a draft from iteration 1, so CI ran during the reviews.

## Iteration 1

Diff: `main...216f4cb`.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3 | DaVinci | After the jump, the note that confirms the correction was out of view | ✅ Yes (`2c98937`) | The note is sticky at the top of the page |

## Iteration 2

Atlas and DaVinci approved `2c98937`.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3 | Atlas, DaVinci | The sticky note could cover the card the page lands on | ✅ Yes (`e56dac3`) | `scroll-margin-top: 96px` on the face cards |

## Iteration 3

Spock read `e56dac3` and ran 14 mutations, of which 2 survived.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3 | Spock | Reversing "next card, else the one before" passed: no test had a face with a card on each side | ✅ Yes (`a317627`) | A list of three: the middle face lands on the next card and the last on the one before; the reversed order now fails |
| P4 | Spock | Removing the note's CSS class passed | ✅ No change needed | CSS only |

## Iteration 4

`a317627` adds only a test. Mozart checked it against Spock's finding (the mutation now fails) and CI.
