# Review of issue #107 — Ignore strangers' faces

Process: [docs/process.md](../process.md). The owner asked to get rid of strangers' faces (posters, crowds) without naming them. Edison added "ignore" for one face, a photo's unnamed faces, an unnamed person and the unnamed people with one or two faces, each behind a confirmation and on the path of "Delete face data" (D-081, D-117). Atlas, DaVinci (the pages are UI) and Spock reviewed it; Atlas approved the design. The earlier rounds are summarised from their commits.

## Iteration 1

Diff: `main...0143a56`, then main merged in (`5b4137a`).

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P2 | DaVinci | The confirmation did not show which faces go | ✅ Yes | Crops of the faces on the confirmation page (`074f97f`) |
| P2 | DaVinci | The confirming button did not look dangerous | ✅ Yes | The danger button style (`074f97f`) |
| P2 | DaVinci | The wording did not say the faces are deleted for good | ✅ Yes | Clearer words in EN and pt-BR (`074f97f`, `8107f68`) |
| P2 | Review | The history could not tell an ignore from a deletion, and the migration's Down had to stay safe | ✅ Yes | An `ignore` scope in the history with a migration whose Down rewrites `ignore` rows to `person` first (`074f97f`, `8107f68`) |

## Iteration 2

Diff: `0143a56...811d1a5`. DaVinci's re-check led to one warning instead of two, a grid for the crops and plain navigation (`8107f68`); D-117 records the history wording as shipped (`811d1a5`).

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3 | Spock | Test gaps on the key rules (named people never touched, the count check on the bulk action) | ✅ Yes | Tests in `2b32b8d`, which also lists a person once per photo and says "and N more people" |

## Iteration 3

Diff: `2b32b8d...7cbc9bf`. After S-038 merged, Edison merged main and regenerated the migration as `20261022201736_IgnoreFaceDeletions`, after `DriveSync` (`69b337b`).

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P2 | Spock | The regeneration dropped Down's safe rewrite, so Down would fail with any `ignore` row | ✅ Yes | The `UPDATE face_data_deletions SET scope = 'person' WHERE scope = 'ignore'` is Down's first statement again (`7cbc9bf`); Spock re-checked it |

## Iteration 4

Mozart merged main (#126's merge direction and #129's deploy fix), keeping both sides of the resource files, and checked the fixes against CI.
