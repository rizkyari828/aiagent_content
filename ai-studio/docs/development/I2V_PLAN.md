# I2V Character Animation — Execution Plan / Cross-Machine Handoff

Status: active plan, created 2026-11-02. Operator-owned.

This file is the single entry point for the next milestone (real character animation). A fresh
session on either machine should be able to start from here without a briefing: read this file,
then `../agent/CHECKPOINT.md` and `../context/STATE.md`.

To execute the work, use the ready prompt for the machine you are on:

- `I2V_AGENT_TASK.md` — Mac scaffold (P0.3, P1.1–P1.5); done 2026-11-02, kept for reference.
- `I2V_HOME_TASK.md` — home GPU steps (P0.1 push, P1.6 real workflow, P2 benchmark).

## 1. Where we actually are

Verified in this checkout (2026-11-02, office Mac):

- `main` is still `10d17d9` (2026-09-24), and the work lives on two pushed branches:
  `docs/video1-state-cleanup` (plan/task docs) and `feat/i2v-scaffold` (built on it:
  Application contracts, ComfyUI adapter, options, capability registration, tests). Fetch and
  merge both on the home machine; `main` does not contain them yet.
- Release build 0 warnings / 0 errors. Full suite against the local PostgreSQL 18.6 Compose
  container (port 5544): 1190 total, 1189 passed, 0 failed, 1 skipped (real audio E2E). Without
  a database the same 10 `AIStudio.Tests.Persistence` tests fail for the missing connection
  string only.
- The render pipeline already consumes video clips: `AssetType.Video` flows through
  `SceneMediaInput`, and both Manim and Blender emit clips (D-028). I2V is therefore
  provider + routing + clip-duration fitting, not new render plumbing.
- `IVideoGenerationProvider` exists (D-046); `visual.character_animation` is registered against
  provider `comfyui-i2v`, disabled by default. No router or handler selects it yet.
- The real workflow JSON files (`i2v_workflow.json`, `i2v_workflow_first_frame.json`) do not
  exist by design. `ComfyUiVideo:WorkflowPath` points at them, so enabling the engine without
  authoring them fails with `video_not_configured` instead of inventing a graph.
- Artifact verification is still an MP4/WebM signature check; replace it with `IMediaInspector`
  once the real output format is confirmed (P3.1).

Reported from the 2026-10-01 home session — NOT verifiable in this checkout (code and
artifacts are absent):

- Anime Video #1 "Angsa Kertas di Ujung Malam": project `0bdba51c-58a9-4fc0-9a68-6f3e130dadba`,
  storyboard `48821f7d-6c82-47f9-9270-e7dd1a4084f9`, 5 scenes, ~72.5 s, 1280x720,
  identity consistent, audio/music/subtitle/master/final render done.
- Ken Burns v2: first implementation (`zoompan`) OOM at ~14 GB RSS / exit 137; replaced with
  streaming `scale eval=frame` + fixed crop; reported 1179 tests passing. Result is camera
  pan/zoom only — no character or object motion.
- That work was unpushed at the time; nothing of it had reached `origin` by 2026-11-02.

Treat reported items as evidence to reconcile, never as verified repository state.

## 2. Machine rules

Mac (office: M2, 8 GB, no GPU, no Ollama/ComfyUI): documentation, C# contracts, unit tests,
fake-HTTP tests, workflow JSON drafting, deterministic self-checks, docs/state maintenance.
Never: model inference, ComfyUI generation, Blender CUDA, VoxCPM, ACE-Step, heavy FFmpeg renders.

Home (RTX 4060 Ti 16 GB, WSL2, ComfyUI 0.36.0, Blender 5.2.2): model download, engine benchmark,
real provider runs, real E2E production.

Do not edit these until the home work from 2026-10-01 is pushed (it touches the same code):
`FfmpegCommandPlan.cs`, `FfmpegVideoRenderer.cs`, `RenderingOptions.cs`, `SceneMediaInput.cs`,
and any `scripts/` helper related to Ken Burns. New files and docs are safe.

## 3. Checklist

Each item states owner, deliverable, and how to verify it. Do not check an item until verified.

### P0 — unblock and baseline

- [ ] **P0.1 [Home] Push the 2026-10-01 work.** Push/merge the Ken Burns v2 + anime Video #1
      branch so `origin/main` contains it.
      Verify: `git log --oneline -5` on `main` shows the Ken Burns commit; solution builds;
      `git status` clean.
- [x] **P0.2 [Mac] Docs reconciliation** (this branch). Canonical anime Video #1 and Ken Burns
      status recorded as reported, "Video #1" ambiguity removed, next milestone points here.
      Verify: links resolve, `git diff --check` clean.
- [x] **P0.3 [Mac] Database baseline.** `ai-studio/.env` with `POSTGRES_PORT=5544` (5432 is taken
      by an unrelated container on this machine), `docker compose up -d`, then
      `dotnet test` with `ConnectionStrings__DefaultConnection`.
      Verify: 0 failed, 1 skipped; exact counts appended to STATE.
      Done 2026-11-02: 1190 total, 1189 pass, 0 failed, 1 skipped (existing migrations applied
      with `dotnet-ef 10.0.12`; no new migration).

### P1 — design and scaffold (Mac)

- [x] **P1.1 ADR.** Add a `DECISIONS.md` entry for the I2V boundary: Application-owned port,
      Infrastructure adapter, trusted repository-owned workflow only, no caller graph/node/path,
      GPU lease held across the whole generation, CPU persist/probe after release.
- [x] **P1.2 Contracts.** `IVideoGenerationProvider`, `VideoGenerationRequest` (animation prompt,
      first-frame asset reference — one pinned identity allowed, duration/fps/size/seed),
      `VideoGenerationResult`, `VideoGenerationException` with stable `video_*` codes, modelled
      on `IImageGenerationProvider`. No paths, URLs, node graphs, or model names in Application.
- [x] **P1.3 Options + DI.** `ComfyUiVideoOptions` (`Enabled=false` default, BaseUrl, WorkflowPath,
      width/height/fps/max seconds/timeout). Provider registered but disabled by default.
- [x] **P1.4 Capability.** Register the provider for existing `visual.character_animation` in
      `ProductionCapabilityCatalog`, enablement driven by options.
      Verify: capability tests (catalog mapping, DI enablement reflection) still pass.
- [x] **P1.5 Fake provider + tests.** `FakeVideoGenerationProvider`; tests for request validation,
      duration/fps bounds, disabled behavior, GPU-lease lifecycle, error mapping, no-path leak.
      Verify: full suite green with no GPU and no model.
- [ ] **P1.6 Workflow template.** Repository-owned JSON under `Rendering/ComfyUI/` for the selected
      engine — only when node names can be verified from local/ComfyUI documentation. Otherwise a
      skeleton plus an explicit `TODO(verify on home)`; never invented nodes or model paths.
- [ ] **P1.7 Benchmark spec.** One scene (shadow raising a hand, golden light growing), 3–5 s,
      fixed seed. Compare LTX-Video 0.9.x 2B vs Wan2.2 TI2V-5B on: face/hand morph, prompt
      adherence, temporal stability, peak VRAM, wall time. Artifacts:
      `.demo/artifacts/i2v-bench-<UTC>/`.

### P2 — benchmark (Home)

- [ ] **P2.1** Install/verify ComfyUI nodes and models (fp8/GGUF for 16 GB). Record versions.
- [ ] **P2.2** Run the P1.7 benchmark for both engines; record artifacts and numbers.
- [ ] **P2.3** GO/NO-GO on the primary engine; record in `DECISIONS.md` and `STATE.md`.

### P3 — implementation

- [ ] **P3.1** `ComfyUiVideoGenerationProvider`: upload first frame, inject prompt/seed/length,
      poll, download, verify with `IMediaInspector`, persist through `IAssetFileStore`.
- [ ] **P3.2** `IGpuResourceGate` held across submit → poll → download; released before CPU work.
- [ ] **P3.3** Durable wiring: hero scenes become video clips, others keep Ken Burns; `SceneAsset.Type=Video`;
      `Creator` fingerprint includes engine + prompt + seed + first-frame identity.
- [ ] **P3.4** Render fit: clip trimmed/looped to the `SceneTiming` scene duration; audio, subtitles,
      transitions unchanged.
- [ ] **P3.5** Failure policy: disabled/timeout/OOM ⇒ Ken Burns fallback, stable codes, render not failed.
- [ ] **P3.6** Recipe: `motion-comic` v2 (or a new recipe) with `visual.character_animation` and
      fallback to `visual.ai_image`; v1 stays immutable.

### P4 — validation and delivery (Home)

- [ ] **P4.1** 1-scene E2E through the HTTP API (enqueue → completed → video asset → render).
- [ ] **P4.2** Full anime E2E: 5 scenes, identity pin `student-01-reference` v1, audio, subtitles,
      `RenderVideo`, `FinalVideoQa`.
- [ ] **P4.3** Human review: face/hand morph, motion believability, Ken Burns vs I2V comparison.
- [ ] **P4.4** Final docs (CHECKPOINT/STATE/DECISIONS) + artifact hashes; merge.

## 4. Handoff protocol

- After each item: append one `STATE.md` entry in the existing format,
  `PASS (<machine> code-only|real) - <item>: <commands> -> <exact counts>`, and update the
  `CHECKPOINT.md` resume line. Update this file's checkbox only when verified.
- Never promote reported-from-other-machine work to verified. Mark it `reported`.
- If docs and code disagree, code wins; fix the doc in the same branch.
- One branch per phase (`docs/*`, `feat/i2v-*`), no force push, no unrelated edits.

## 5. Definition of done

- At least one hero scene in a real anime production is generated by a local I2V engine,
  identity preserved, with Ken Burns as automatic fallback.
- `FinalVideoQa` passes on the final MP4; full suite green; benchmark numbers and the
  engine decision recorded in `DECISIONS.md`.
- A fresh agent can resume from this file + CHECKPOINT alone, on either machine.

## 6. Open decisions (human)

1. Primary engine after the benchmark (LTX 2B vs Wan2.2 TI2V-5B) and acceptable quality bar.
2. Clip length policy: fixed 3–5 s loop vs per-scene length from `SceneTiming`.
3. Selection surface: per-scene API flag (`auto` | `kenburns` | `i2v`) vs recipe capability only.
4. Whether the fallback is per-scene (preferred) or per-production.
