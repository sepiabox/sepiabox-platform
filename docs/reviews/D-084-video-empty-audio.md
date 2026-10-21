# Review of D-084 — Silent 720p versions for videos whose sound stream holds nothing (no story)

Process: [docs/process.md](../process.md). Atlas knows the project; Spock sees only the diff. Why: the owner's first full run reported hundreds of camcorder `.MOD` and `.MPG` files with "Nothing was written into output file, because at least one of its streams received no packets"; `ffprobe` lists an AC-3 stream, and the file plays without sound.

## Iteration 1

Diff: `origin/main...a4cb278`. Atlas reproduced the failure with the test's recipe (exit 69 with sound, exit 0 and a video-only MP4 without), checked D-045 and D-049 (the poster already ignores sound; the web player has no audio-specific code) and that a rerun of the same `process` redoes only failed files.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3 | Atlas | The retry without sound follows any failure, so a one-off NVENC error could silently drop good sound | ✅ Yes (`324f1a4`) | "Made without sound <path>: <message>" listed per file and counted in the summary; test with a fake one-off error |
| P3 | Atlas | `2>&1 \| Tee-Object` is the wrong logging recipe on Windows PowerShell 5.1 | ✅ Yes (`324f1a4`) | operations.md: `\| Tee-Object -FilePath process-log.txt`, stdout only, and why |
| P4 | Atlas | The test's byte surgery could throw on a tiny packet | ✅ Yes (`324f1a4`) | Guard |
| P4 | Atlas | The quoted FFmpeg message is shortened | — | Accepted: "received no packets" is the phrase to search for |
