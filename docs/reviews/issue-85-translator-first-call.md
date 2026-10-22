# Review of the fix for issue #85 — The question translator's slow first call

Process: [docs/process.md](../process.md). Edison implemented it while track B of Sprint 14 waited on reviews. Atlas, Spock and DaVinci reviewed it; DaVinci ran `ask.js` in headless Chromium against a slow local server. The prompt and the output format are unchanged, so no live recording was needed.

## Iteration 1

Diff: `132976c...b1ebc4c`.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P2 | DaVinci | After the browser's Stop, or a dropped connection, the button stayed disabled and "Thinking…" stayed on screen | ✅ Yes (`8b7430e`) | A 100 s client give-up; typing clears a stale state; after the 8 s slow line, a second press goes through (`5468186`) |
| P2 | Atlas | Anthropic's docs: a compiled grammar is cached for 24 hours from last use, so the slow call follows any day without questions, not only a deploy | ✅ Yes (`5468186`) | Docs reworded; a page-load warm-up (`QuestionWarmUp`) when nothing has been translated for 12 h, single-flight, outside the daily quota, logging only outcomes and tokens |
| P3 | DaVinci | A status region shown from `display:none` is not reliably announced; disabling the focused button lost focus; Firefox may keep a disabled button after Back | ✅ Yes (`8b7430e`) | An always-rendered status region with texts from `data-` attributes; `aria-disabled` instead of `disabled`; `pageshow` resets every form; `aria-busy` dropped (`5468186`) |
| P3 | Atlas | No overall deadline: per request 90 s with a retry could outlive Container Apps' 240 s ingress | ✅ Yes (`5468186`) | `AnthropicQuestionOptions.Deadline` = 200 s over every request and retry; past it the outcome is Unavailable |
| P4 | DaVinci, Atlas | The long-wait sentence on every question; the 100 s message while the request is still alive; a spinner | ✅ Yes (`8b7430e`, `5468186`) | The long-wait line after 8 s; "Still waiting. You can keep waiting or try again."; a spinner that respects reduced motion |

## Iteration 2

Atlas re-checked `5468186` and approved it, with three P4s that were fixed in `f3d7461`: any answered call counts as warm, the 12 h state is per instance, and the warm-up's spend is logged but not in `question_usage`. Spock reviewed `f3d7461` and found no P0–P2. His P3 test gaps were filled in `df7fe97`: the back-off after a throw, the 12 h + 1 s boundary, a translated outcome with no tokens, and the wiring. While filling them Edison found and fixed a bug: the timeline started a warm-up for an empty library that shows no question box. `df7fe97` also merges main (S-027). Mozart checked the last changes against the diff and CI. `ask.js` has no automated test (the repository has no JavaScript test setup); DaVinci's browser run is its check.
