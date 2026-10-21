# Review of D-078 — Faster `process` (no story)

Process: [docs/process.md](../process.md). Atlas knows the project; Spock sees only the diff. Neither reviewer called the real API. Why: the owner's first full run did about 0.9 files a second with the CPU at 98%, the drive and the GPUs nearly idle, so the 87,000-file drive would take more than a day.

## Iteration 1

Diff: `f02ca83...62a0d49`. Atlas ran the old and the new build on 8 photos of awkward sizes into separate work folders: manifests and previews byte-identical, thumbnails different (as D-078 says), so the owner can stop the running sequential `process`, update and resume on the same work folder. Spock checked races on the state store, manifests and derivative folders, the same-content wait for deadlocks, cancellation (`.tmp` and rename, exit code 130), the shared ONNX sessions and the read-only source, and mutated the same-content wait.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3 | Spock | A video held a job slot while it waited for the single video slot, so photos behind videos stalled | ✅ Yes (iteration 2) | A streamed file waits for its video turn before it takes a job slot; the reader hashes videos as a stream; test with a held transcode |
| P3 | Spock | The same-content ordering was only caught by chance (removing it passed 1 of 3 runs) | ✅ Yes (iteration 2) | 50 copies and one that changes at `--jobs 8`, the first copy held back; fails deterministically without the wait |
| P3 | Atlas | ONNX Runtime kept its default intra-op pool with spinning in every job, on top of Magick's threads | ✅ Yes (iteration 2) | With jobs > 1: `processors ÷ jobs` intra-op threads, spinning off; Magick's limit restored afterwards; real-model test of jobs 1 against 4 (byte-identical faces here, CLIP in CI) |
| P4 | Spock | The determinism test ran without models | ✅ Yes (iteration 2) | Whole `ProcessResult` compared with fake embedder and face detector |
| P4 | Spock | Every photo went into one byte array; one over 2 GB failed | ✅ Yes (iteration 2) | Photos larger than the read-ahead are streamed from their path |
| P4 | Spock | Magick's thread limit was process-wide and never restored | ✅ Yes (iteration 2) | Restored in `finally` |
| P4 | Atlas | Memory per job not documented | ✅ Yes (iteration 2) | operations.md and D-078: about 100 MB at 24 MP, 400 MB at 50 MP, plus 256 MB read-ahead |
| P4 | Atlas | DirectML not worth doing now | — | Deferred in D-078 until the owner measures `--jobs`; `LiveGpu` check and Windows ML noted |

## Iteration 2

Diff: `62a0d49...ae4e975` (after merging main for S-019). Atlas found nothing and confirmed resuming is still safe. Spock re-ran the mutation (fails 3 of 3), found no deadlock in the new slot order and judged the reader's streamed hashing acceptable for a hard disk.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P4 | Spock | While one video is transcoded the reader may hash the next, so two streams read the drive | — | Accepted and noted in D-078: a transcode is mostly CPU and GPU work |
| P4 | Spock | `ModelThreads` was inserted between `ModelSession`'s summary and the class | ✅ Yes | Record moved above with its own summary |

Decision: no findings open → pull request.
