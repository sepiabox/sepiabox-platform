# Review of issue #94 — Choose the sound stream that decodes; report a damaged photo once

Process: [docs/process.md](../process.md). Tesla fixed it (D-105) after the owner's `process` run: three MPG files failed because their first sound stream (AC-3) had only five broken packets while a second stream (MP1) was fine, and one corrupt CR2 failed on every run. Atlas and Spock reviewed it. The PR was a draft from iteration 1, so CI ran during the reviews.

## Iteration 1

Diff: `main...6880ab6`.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P2 | Atlas | `IsDamage` took blob, delegate and missing-delegate errors as damage: a drive that drops out, a permission problem or a missing codec would mark good photos `Damaged`, a status that sticks until `--retry-left-out` | ✅ Yes (`d0681d6`) | Only corrupt-image and coder errors are damage (the owner's CR2 error comes from libtiff/libjpeg, a coder error); the rest stay retryable failures; D-105 says why |
| P3 | Atlas | No test pinned which exceptions are damage | ✅ Yes (`d0681d6`) | Eight exception types, three of them damage; the end-to-end test uses a real JPEG with overwritten data |
| P4 | Atlas | The owner-facing text should be pt-BR where it reaches the UI | ✅ No change needed | The text is in the English CLI only |

## Iteration 2

Atlas re-checked `d0681d6` and approved it. Spock ran 21 mutations, of which 5 survived (2 of them equivalent).

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3 | Spock | The fallback to no sound when a later stream passes the check but its encode fails was untested (C11) | ✅ Yes (`d7ba6f6`) | A wrapped `ffmpeg` fails only the encode of the second stream: the 720p version has no sound and the problem says so |
| P3 | Spock | `VideoDerivatives.AudioStream` was never read by a test (C12) | ✅ Yes (`d7ba6f6`) | Real FFmpeg with the second stream chosen: sound kept, stream 1, a proxy with video and audio |
| P4 | Spock | The "not read again unless it changes or `--retry-left-out`" text was not pinned (D9) | ✅ Yes (`d7ba6f6`) | Asserted |

## Iteration 3

`d7ba6f6` adds only tests and merges main (S-045). Mozart checked it against Spock's findings and CI. Not verified here: whether the owner's real CR2 throws a coder error; the owner's next `process` run shows it, and if it reports a failure instead of a damaged file, its exception type is a follow-up.
