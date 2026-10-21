# Review of D-087 — NVENC preflight and the without-sound fallback

Process: [docs/process.md](../process.md). Atlas knows the project; Spock sees only the diff. Media in the tests is generated in the tests; no owner file is committed.

## Why

D-084 read FFmpeg's last line, "at least one of its streams received no packets", as camcorder files declaring an empty AC-3 stream, and made those videos without sound. The owner's checks showed otherwise on one of the `.MOD` files:

- ffprobe counted 2010 AC-3 packets at 48 kHz, stereo.
- VLC played the sound.
- libx264 made a 720p version with sound.
- Our exact arguments with `h264_nvenc` failed with "Driver does not support the required nvenc API version. Required: 13.1 Found: 13.0", because the driver was 592.82 and FFmpeg 9.0.2 needs 610.00 or newer.

The last line was only the tail of an encoder failure. After updating the driver to 621.01, the same command made a 720p version with sound.

## Iteration 1

Diff: `c3d69af...89488ab`. Atlas checked D-084, D-036, D-045 and D-078, the decision not to fall back to libx264, and the cost of the owner's next run. Spock tried 11 mutations; 6 were caught.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3 | Spock | D-087's central rule was untested: going back to "any failure makes a version without sound" passed every test | ✅ Yes (iteration 2) | Test where only the transcode with sound fails and the sound decodes: `Failed`, sound kept |
| P3 | Atlas + Spock | The redo re-encoded every video whose source never had sound | ✅ Yes (iteration 2) | The source's sound is checked first; no stream or undecodable sound is recorded without an encode |
| P3 | Atlas | `--jobs` could exceed the GPU's NVENC session limit | ✅ Yes (iteration 2) | At most 4 NVENC encodes at once across the process, the preflight included; libx264 unlimited |
| P4 | Atlas + Spock | Long FFmpeg messages kept only the end, losing the cause | ✅ Yes (iteration 2) | First 300 characters, "…", last 699 |
| P4 | Spock | A momentary read failure (USB) counted as undecodable sound | ✅ Yes (iteration 2) | Input and I/O failures throw instead; nothing is recorded and the next run checks again |
| P4 | Spock | A failed delete of the preflight file could hide the encoder's error | ✅ Yes (iteration 2) | `IOException` and `UnauthorizedAccessException` caught |
| P4 | Spock | No test for a failing sound check, a failed redo, or an unreadable 720p | ✅ Yes (iteration 2) | One test each |
| P4 | Atlas | operations.md did not say how to check the driver | ✅ Yes (iteration 2) | `nvidia-smi --query-gpu=name,driver_version --format=csv` |

## Iteration 2

Diff: `89488ab...1f89aed`. Both approved. Spock re-ran all mutations plus four new ones: all 14 caught.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P4 | Spock | The session limit is static, so the "at least 2 at once" bound of its test could flake beside another NVENC test | ✅ Yes (`54e2331`) | Only the upper bound is checked; removing the limit still fails the test |
| P4 | Atlas | The read-failure check matches English FFmpeg and OS phrases | ➖ Stays as is | A localized error would only keep a silent 720p that exists; the stream probe fails first when the drive is gone |

main (S-017, D-086) was merged in as `a85ff30`; decisions.md reads D-085, D-086, D-087.
