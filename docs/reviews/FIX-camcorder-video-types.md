# Review of the camcorder video fix

Process: [docs/process.md](../process.md). Atlas knows the project; Spock sees only the diff. Not a backlog story: a fix so the owner's archive (1,692 `.MOD`, 2,031 `.MTS`, 1,461 `.MPG`, 2,318 `.CR2`) is processed correctly before the full run (D-045).

## Iteration 1

Diff: `main...fix/camcorder-video-types` at `1eae69b`.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P1 | Spock | Odd output widths after applying the sample aspect ratio | — | False positive: `Fit()` rounds both sides to even |
| P1 | Atlas | Videos already processed are never regenerated with the new filters | Justified | The owner has only run `scan`; derivatives versioning is recorded as a follow-up in D-045 |
| P2 | Atlas | JVC `.MOD` may flag 16:9 only in `.MOI` sidecars, gone after renaming | ✅ Yes (iteration 2) | Record the limitation; owner asked for ffprobe samples; per-source override is a follow-up |
| P2 | Atlas+Spock | CR2 fixture not like a real Canon file; no real CR2 test | ✅ Yes (iteration 2) | Four-IFD fixture; real-file test a follow-up (the proxy blocks raw.pixls.us) |
| P2 | Spock | Poster seek writes nothing for very short clips | ✅ Yes (iteration 2) | Retry at `-ss 0`, else keep the manifest and report a problem |
| P2 | Atlas | No rotation integration test | ✅ Yes (iteration 2) | Test |
| P3 | Atlas+Spock | Progressive footage untouched not asserted; weak interlaced fixtures | ✅ Yes (iteration 2) | idet and SSIM checks; woven 25i fixtures |
| P3 | Atlas | D-015 and D-029 not marked as amended | ✅ Yes (iteration 2) | "Amended by D-045" |
| P3 | Spock | DVD menu VOBs and non-video `.mod` treated as videos | ✅ Yes (iteration 2) | Menus are Other; files with no video stream are left out |
| P4 | Atlas+Spock | `video/dv`, download-name rows, yadif mode notes | ✅ Yes (iteration 2) | Fix |

Decision: P2 open → iteration 2.

## Iteration 2

Diff: `1eae69b...e435839` (5 commits). Atlas ran the video integration tests on FFmpeg 6.1.1 (12/12) and confirmed items 1–7 and 9–11 resolved; item 8 not (below).

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P1 | Atlas+Spock | A file later found to have no video keeps an earlier manifest, which is uploaded and catalogued | ✅ Yes (iteration 3) | `RemoveSource` in the NotVideo branch; two-run test |
| P2 | Spock (Atlas P3) | Tracker `.mod` detection depends on an FFmpeg build with libopenmpt; untested with a real module | ✅ Yes (iteration 3) | Sniff the tag at offset 1080; test with a generated module |
| P2 | Spock | Missing width/height in the probe throws an uncaught exception | ✅ Yes (iteration 3) | `TryGetProperty`; `VideoProcessingException` |
| P2 | Spock | CR2 fixture puts IFD0 away from offset 16, so real CR2s may take a different ImageMagick coder | ✅ Yes (iteration 3) | IFD0 at 16; check the coder used |
| P2 | Spock (Atlas P3) | Poster fallback test doesn't prove the fallback ran | ✅ Yes (iteration 3) | Drop `short.mts`; check the first frame was used |
| P3 | Atlas | Retry skipped when the seek exits non-zero | ✅ Yes (iteration 3) | Catch and retry |
| P3 | Spock | Stale or empty temp poster frame | ✅ Yes (iteration 3) | Delete first; check length |
| P3 | Spock | New exception subtype may be swallowed by existing catches | ✅ Yes (iteration 3) | Check catch sites |
| P3 | Spock | `video/x-dv` dropped from the download map | ✅ Yes (iteration 3) | Keep both |
| P3 | Spock | Attached cover picture accepted as video | ✅ Yes (iteration 3) | Check `attached_pic` |
| P3 | Spock | SSIM 0.98 may be flaky | ✅ Yes (iteration 3) | 0.95 |
| P3 | Spock | Counters may race if files run in parallel | ✅ Yes (iteration 3) | Check; `Interlocked` |
| P3 | Atlas | Summary line has no left-out count | ✅ Yes (iteration 3) | Add it |
| P3 | Atlas | No real CR2 test | ➡️ Follow-up | Download test with pinned SHA; manual check in operations.md now |
| P3 | Spock | Large non-video files hashed in full before being left out | ➡️ Follow-up | Probe before hashing |
| P3 | Spock | `-display_rotation` needs a recent FFmpeg | — | CI has 6.1 (Atlas confirmed); ignored |
| P4 | Atlas+Spock | `\d`; plural; Windows skip of the stand-in test; D-045 too long; scan counts tracker `.mod` | ✅ Yes (iteration 3) | Fix |

Decision: P1 and P2 open → iteration 3.

## Iteration 3

Diff: `e435839...0e7c00a` (4 commits). Atlas ran the build, format, Pipeline tests and the 18 video integration tests on FFmpeg 6.1.1 and confirmed every iteration-2 row as resolved; Atlas found no new P0–P2.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P2 | Spock (Atlas P3/P4) | Leaving a file out deletes its manifest and is remembered forever; a damaged clip or a false tracker match would silently vanish | ✅ Yes (iteration 4) | No match on MPEG-PS/TS headers; re-probe with a larger window; list left-out paths with the reason; document how to retry |
| P2 | Spock (Atlas P3) | Removal test proves nothing about derivatives and covers only one source | ✅ Yes (iteration 4) | Two-source test with a real derivatives folder |
| P2 | Spock | Counters race if files run in parallel | — | False positive: the loop is sequential |
| P2 | Spock | Callers catching only `VideoProcessingException` let the new type escape | — | False positive: the only probe call site handles it |
| P3 | Atlas | Tracker rule lives in Ingest; the cloud worker would not apply it | ✅ Yes (iteration 4) | Move to Pipeline; note in S-036 |
| P3 | Atlas | Manual CR2 check could leave a temp source in the real work folder | ✅ Yes (iteration 4) | Throwaway folders |
| P3 | Spock | Tracker tag list accepts leading zero; missing tags; read errors propagate | ✅ Yes (iteration 4) | Digit 1–9; tests; return false on I/O errors |
| P3 | Spock | Fake ffmpeg depends on argument position; random-bytes damaged file; null FFmpeg folder; fixture `Patch` position; unexplained SSIM change | ✅ Yes (iteration 4) | Fix |
| P3 | Spock (Atlas P4) | `-select_streams V` could replace the stream index plumbing | — | Optional simplification; current code correct (Atlas) |
| P3 | Spock | A truncated poster PNG is accepted | — | Accepted: decoding it next fails with a problem |
| P4 | Atlas+Spock | Version-dependent keyframe case; untagged Soundtracker modules; record shape; optional stream index; `video/x-dv` comment; non-ASCII digit test; D-045 length | ✅ Yes (iteration 4) | Fix or document |

Decision: two P2 open → iteration 4.

## Iteration 4

Diff: `0e7c00a...f80d2d6` (3 commits). Atlas ran the Pipeline and video integration tests on FFmpeg 6.1.1 (21/21) and confirmed every iteration-3 row as resolved, except the D-045 length (accepted: one decision with numbered parts) and the retry advice (below).

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P2 | Atlas | The documented retry (`touch`) changes the owner's originals and their only date evidence; a Windows re-copy keeps the time, so it doesn't work | ✅ Yes (iteration 5) | `process --retry-left-out`; update the docs |
| P2 | Spock | A damaged MPEG file that ffprobe opens with zero streams would be left out instead of reported | ✅ Yes (iteration 5) | MPEG-looking files are never left out; test with a stand-in ffprobe |
| P2 | Spock | `List<T>.Add` races if files run in parallel | — | False positive: the loop is sequential |
| P3 | Atlas | Video found late gets width 0 on the first probe and no retry | ✅ Yes (iteration 5) | Retry the longer probe on a missing size |
| P3 | Atlas+Spock | The reason is lost on later runs; every left-out file is listed on every run | ✅ Yes (iteration 5) | Store the reason; list new ones, count the rest |
| P3 | Spock | Retry on cover art; reason by message text; cover-art inferred too broadly; MPEG guard doc overstated | ✅ Yes (iteration 5) | Reason enum; retry only on no stream; shared MPEG check |
| P3 | Spock | Test names and checks: "without reading it", "other copy kept", FFmpeg folder | ✅ Yes (iteration 5) | Fix |
| P4 | Atlas+Spock | `ScanProblem` reuse; null-forgiving; seekable stream; docs; pattern parentheses; `using`; CLI listing test; Soundtracker wording | ✅ Yes (iteration 5) | Fix |
| P4 | Atlas | D-045 grew | — | Accepted: one decision with numbered parts |

Decision: two P2 open → iteration 5.

## Iteration 5

Diff: `f80d2d6...48c4e1a` (2 commits). Atlas ran the build, format and the changed Ingest/Pipeline tests on FFmpeg 6.1.1, checked real sound-only AVCHD and VOB bytes against `MpegSignature`, and confirmed every iteration-4 row as resolved; Atlas found no new P0–P2 and accepted a sound-only MPEG file being a problem on every run.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P2 | Spock | Files left out by earlier builds (no stored reason) are never read again under the new MPEG rule | ✅ Yes (iteration 6) | Read rows without a reason once more; test with an old database |
| P2 | Spock | First probe sees video without size, longer probe sees none → left out | ✅ Yes (iteration 6) | A problem, not left out; stand-in test |
| P3 | Spock | Any `00 00 01 xx` counts as MPEG (an MP4 box can start that way); two TS sync bytes can match a module title | ✅ Yes (iteration 6) | MPEG start codes only; three syncs when long enough |
| P3 | Spock | Unreadable file during the signature check is left out | ✅ Yes (iteration 6) | A problem |
| P3 | Atlas+Spock | Redundant hint with `--list-left-out`; no headline for newly left-out files | ✅ Yes (iteration 6) | Fix |
| P3 | Spock | Probe output assumed to hold only video streams | ✅ Yes (iteration 6) | Comment or filter |
| P4 | Atlas+Spock | Enum default; table creation; double blank line; signature reads | ✅ Yes (iteration 6) | Fix |
| P4 | Spock | Result tuple, message-substring asserts, script quoting, file location, fixture test | — | Accepted for this fix |

Decision: two P2 open → iteration 6 (the last allowed).

## Iteration 6

Diff: `48c4e1a...96c4ff1` (2 commits). Atlas ran the Pipeline tests (274 passed, 5 CLIP skips) including the video integration tests on FFmpeg 6.1.1, and the Ingest tests (135/135). Atlas checked real FFmpeg output against the tightened signature rules: MPEG-2 PS `.mod`, MPEG-1 `.mpg`, DVD `.vob` and a sound-only `.mod` start with `00 00 01 BA`; AVCHD `.mts` with 192-byte packets matches the 4/196/388 sync rule; plain `.ts` matches 0/188/376. Every iteration-5 row is resolved; Atlas found nothing new.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P2 | Spock | The "first probe saw video" flag is wrong if `NoVideoStreamException` derives from `VideoProcessingException` | — | False positive: both are separate sealed classes |
| P2 | Spock | The old-database test could pass without reaching the new rule | — | False positive: with the rule removed the test fails (checked by mutation) |
| P2 | Spock | A left-out row saved with no or `Unknown` reason would be re-read forever | — | False positive: every `NoVideoStreamException` is built with an explicit reason |
| P2 | Spock | `MpegSignature.IsMatch(string)` now throws; other callers could break | — | False positive: its only caller wraps it; the tracker check uses the stream overload |
| P3 | Spock | Unguarded `ALTER TABLE` | — | False positive: guarded by `pragma_table_info` |
| P3 | Spock | An MP4 whose first box is 0x1BA bytes long still matches the start code (safe direction); audio PES ids included; two-sync fallback for tiny files; message wording when the first probe output was malformed; report header with earlier files; hint constants; test couplings; explicit enum values | ➡️ Follow-up | One follow-up issue |
| P4 | Spock | Naming, `ParseReason` allocation, enum-order test, `RandomAccess.Read` | — | Accepted |

Decision: no P0–P2 open → pull request.
