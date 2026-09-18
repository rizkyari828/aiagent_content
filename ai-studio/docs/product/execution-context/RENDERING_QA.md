# Video #1 Execution Context — Rendering / QA

**Derived from v0.9.2 sections:** 20–22, 26–28, 34, 47, 49D, 54.  
**Purpose:** bounded context for FFmpeg composition and final QA. No new requirements are introduced here.

## What v0.9.2 requires

- **FFmpeg** is the initial deterministic composition engine and an Architecture Freeze decision.
- Video #1 composition uses FFmpeg.
- The NOW roadmap requires **FFmpeg Recipe → Final QA → Video #1**.
- Heavy generative video is deferred.
- Video artifacts should be checked for basic media integrity, including expected duration, video stream, required audio, and successful decoding/probing.
- Deterministic validation should cover items such as schema, required fields, paths, assets, duration, and encoding.
- Expensive completed stages should be resumable rather than regenerated unnecessarily.

## What v0.9.2 does not specify

The PRD does not freeze an FFmpeg command template, codec/bitrate, target resolution, subtitle format, transition library, or render-manifest schema.

Those implementation details should be the smallest version needed for the first publishable artifact.

## Minimal reading path

1. This file.
2. `../parts/04_JOBS_AND_ARTIFACTS.md` for artifact integrity/resumability.
3. `../parts/05_VIDEO1_PRODUCTION_PIPELINE.md` for rendering progression.
4. `../parts/12_ADR_FREEZE_AND_FINAL_RULES.md` if an architectural change is proposed.
