# Video #1 Execution Context — Narration / TTS

**Derived from v0.9.2 sections:** 26, 27, 47, 48, 56.  
**Purpose:** bounded context for narration work. No new requirements are introduced here.

## What v0.9.2 requires

- Narration is a stage after assets in the target content pipeline.
- For Video #1, narration may be **manual or simple local TTS**.
- The NOW roadmap includes **Narration** before Subtitle and FFmpeg Recipe.
- Optional TTS is listed as a possible improvement for Videos 2–10.

## What v0.9.2 intentionally leaves open

- The **TTS model** is an intentionally deferred decision.
- The PRD does not define a narration provider interface, audio format, voice, sample rate, chunking policy, or persistence schema.

Do not treat a particular TTS engine as frozen by v0.9.2 unless a later approved decision or current codebase contract establishes it.

## Minimal reading path

1. This file.
2. `../parts/05_VIDEO1_PRODUCTION_PIPELINE.md` for surrounding workflow.
3. `../parts/04_JOBS_AND_ARTIFACTS.md` if narration becomes a durable artifact/job.
