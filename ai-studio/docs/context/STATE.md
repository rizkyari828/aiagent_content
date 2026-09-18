# Work state

Updated: 2026-09-18.

## Current milestone

Video #1 is DONE. One real ContentProject executed the existing vertical slice end to end and produced a playable MP4.

## Implemented

- Video Quality v1.1 (renderer tuning + first Visual Asset Engine, no schema/migration):
  - Subtitle: `SubtitleStyle` defaults lowered from FontSize 22 / Outline 2 / MarginV 40 to FontSize 16 / Outline 1 / MarginV 18. Note libass renders SRT through a 288-high script canvas (≈2.5x scale at 720p), so MarginV 18 is a ~45px safe bottom margin; the earlier 40 sat ~100px up into the visuals.
  - Motion: default cycle is now centered zoom only (`slow_zoom_in` / `slow_zoom_out`, 8% travel); `pan_left` / `pan_right` are opt-in via `SceneMediaInput.Motion` and only used for visuals with declared safe margins. No computer vision.
  - Transition: default crossfade reduced 0.5s -> 0.35s; `cut`/`fade`/`crossfade` unchanged.
  - Music/ducking: background music now gets `highpass=f=120` and default volume 0.28; ducking ratio 8 -> 4. Narration is gain-staged with `loudnorm=I=-16:TP=-1.5:LRA=11,aresample=48000,asetpts=PTS-STARTPTS` **only when the render mixes audio**, so a quiet narration is audible and reliably triggers ducking. `asetpts` fixes a loudnorm timestamp offset that truncated the mix under `amix duration=first`; `aresample` avoids loudnorm's 192kHz output. A narration-only render is byte-for-byte unaffected.
  - Visual Asset Engine: `SceneVisualSvg` (pure, Application) composes a structured `SceneVisualBrief` into 1280x720 SVG (Hero/Cards/Window/Chat layouts, controlled palettes, built-in icons); `FfmpegSceneVisualRenderer` (Infrastructure) rasterizes it to PNG through the existing `IProcessRunner`/FFmpeg `librsvg` decoder. No new runtime, model, or NuGet dependency. Visuals reserve the bottom ~90px for subtitles.
- Demo artifact: `src/AIStudio.Api/assets/renders/vq1-demo/quality-v1.1.mp4` (2113072 bytes, 24s, 1280x720, h264/aac 48kHz stereo, styled subtitles burned in, SHA-256 `a7821689f38f039c6197b6c1bde2328395c5734fd34c0af1ab8b386f547402cc`), rendered by the real `FfmpegVideoRenderer` against the canonical Video #1 narration/subtitle and 8 composed visuals (`assets/vq1-demo/visuals/`), with an original locally synthesized music bed (`assets/vq1-demo/music_v1_1.wav`). Canonical Video #1 and `quality-v1.mp4` are untouched.
- Video Quality v1.2 (transition ghosting fix, no schema/migration): optional `SceneMediaInput.VisualKind` (`SceneVisualKind.Photographic` default, `Graphic` for text-heavy/UI/infographic) makes `FfmpegCommandPlan` choose the crossfade type per boundary. A boundary touching a `Graphic` scene uses `xfade transition=fadeblack` (fade through background) instead of `fade`, eliminating double-text ghosting; photographic boundaries keep `fade`. `cut`/`fade` and the 0.35s overlap (hence scene timing/offsets) are unchanged. Classification is deterministic and explicit; no computer vision or new dependency.
- Demo artifact: `src/AIStudio.Api/assets/renders/vq1-demo/quality-v1.2.mp4` (2012269 bytes, 24s, 1280x720, h264/aac 48kHz stereo, styled subtitles burned in, SHA-256 `4babcdf33f592c9a82fc60f55af12c0118fd4f50315cf4ae3f5d8952856b79f8`), rendered by the real `FfmpegVideoRenderer` against the v1.1 composed visuals marked `Graphic` so every boundary fades through background. `quality-v1.1.mp4` and canonical Video #1 are untouched.
- Video Quality v1 (renderer quality pass, no schema/migration): deterministic scene motion (`none`, `slow_zoom_in`, `slow_zoom_out`, `pan_left`, `pan_right` via `zoompan`), controlled transitions (`cut` concat, `fade` per-scene through black, `crossfade` via `xfade` with overlap-compensated scene duration), libass-styled burned-in subtitles (`subtitles` filter + `force_style`), and optional config-driven background music with `sidechaincompress` ducking plus renderer-supported per-scene SFX (`adelay` + `amix`).
- Subtitle decision: styled subtitles are burned in and no soft `mov_text` stream is embedded, because FFmpeg's MP4 muxer forces a lone subtitle track to `default=1` and would double-render on top of the burn-in. `RenderVideoResult.SubtitleBurnedIn` records this and `FinalVideoQaJobHandler` skips the soft-stream requirement only for burned-in subtitles; older results default to `false` and keep the previous behavior.
- Demo artifact: `src/AIStudio.Api/assets/renders/vq1-demo/quality-v1.mp4` (1077280 bytes, 24s, 1280x720, h264/aac, no soft subtitle stream, SHA-256 `cbe6958593212fb024bf6239998f920f44a41558b54f6213f9df25b0a584fc9e`), rendered by the real `FfmpegVideoRenderer` against the canonical Video #1 scene/narration/subtitle assets and an original locally synthesized music bed (`src/AIStudio.Api/assets/vq1-demo/ambient_test.wav`). The canonical Video #1 MP4 was not touched.
- Video #1 E2E run: ContentProject `01b7f0ef-e749-4e1d-bbad-a93d016b2be2`; GenerateIdea -> GenerateScript -> approved ReviewedScript -> GenerateStoryboard (8 scenes) -> 8 scene assets -> narration -> subtitle -> RenderVideo -> FinalVideoQa.
- Project AI stayed local (Ollama `qwen3.8:27b-q4_K_M`); no paid or external provider was used.
- Config correction: `Ollama:TimeoutSeconds` raised 120 -> 600. The validated model generates at roughly 9 tokens/second, so the GenerateScript 3000-token budget exceeded the old 120s client timeout and failed with `ai_timeout` across all retries; the existing `1..600` validation bound already anticipated this.
- Routine reasoning off: the typed `AiTextRequest` now carries an optional `Think` flag that maps to the Ollama `/api/chat` `think` field. `GenerateScriptJobHandler` requests `Think: false`; callers that leave it null keep the previous thinking behavior. A fresh GenerateScript job (`012aaac4-fdcf-4b2f-b3a6-82f4d42e7c60`) completed with `retryCount` 0 in ~76s / 648 output tokens; the earlier failed job (`196926e0-f9a1-48f7-a360-ac52ec320945`) is preserved untouched.
- Artifact: `src/AIStudio.Api/assets/renders/01b7f0efe7494e1dbbada93d016b2be2/1526be4b6d434f54999782cc368a1990.mp4` (499638 bytes, 24s, 1280x720, h264/aac/mov_text, SHA-256 `2a1e6980da36edadbbed375bbde8fd802669892b1ca2ecfcb726ef74b7884fb8`).
- Untracked local media under `src/AIStudio.Api/assets/` was intentionally preserved for manual inspection and is not committed.

## Verification

- PASS - 278/278 tests (0 failed, 0 skipped) with `ConnectionStrings__DefaultConnection` set; 86 focused `AIStudio.Tests.Rendering` tests cover motion mapping/safety default, the transition ghosting policy, transition selection/duration, subtitle styling/burn-in, music/ducking/SFX argument generation, the SVG composer (all layouts + escaping), the librsvg rasterizer boundary, and backward compatibility when new options are absent.
- PASS - `dotnet build AIStudio.slnx -c Release` 0 warnings/0 errors; `dotnet format AIStudio.slnx --verify-no-changes` clean; `git diff --check` clean.
- PASS - Video Quality v1.1 demo re-probed with `ffprobe`: 24s, 1280x720, h264, aac 48kHz stereo, styled subtitles burned in, full decode clean, SHA-256 recorded above. Mix loudness mean -18.1 / peak -13.8 dB, narration gain-staged and music ducked beneath it (no clipping).
- PASS - audio ducking calibrated on the real assets: with narration gain-staged and ratio 4, the ducked music peaks ~-33 dB, ~20 dB below the narration, and recovers between phrases.
- PASS - `librsvg` SVG->PNG rasterization verified in the installed FFmpeg 6.1.1 build (`svg_pipe` format, `librsvg` decoder).
- PASS - canonical Video #1 MP4 and `quality-v1.mp4` re-probed with `ffprobe`; SHA-256 values unchanged.
- PASS - `/health/ready` returns HTTP 200 (PostgreSQL Healthy) and the persistent job worker is active.

## Known issues and next task

- `FinalVideoQa` `Job.Result` is persisted but not deserialized by `GET /api/jobs/{jobId}` (`GenerateIdeaWorkflow.FindJobAsync` omits `JobType.FinalVideoQa`); read it from PostgreSQL until a focused fix is authorised.
- The Visual Asset Engine is a reusable composer/rasterizer, but `RenderVideo` still requires an existing `SceneAsset` per scene. Wiring automatic visual generation into the durable asset flow (and letting Qwen emit `SceneVisualBrief` direction) is a future task; the v1.1 demo exercises the engine directly against the real renderer.
- Demo visuals remain static stills with deterministic FFmpeg motion; there is no AI image generation, 3D, or character animation.
- Next milestone: manual publishing.
