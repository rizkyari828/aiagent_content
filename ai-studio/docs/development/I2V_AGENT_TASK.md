# I2V Scaffold — Execution Prompt (Mac session)

Use this file to run the next step in a **separate session**, without re-briefing.

- Target machine: office Mac (M2, 8 GB, no GPU, no Ollama/ComfyUI).
- Model routing (local-ai-infra `ai_infra_route`, 2026-11-02): **BALANCED / high → `deepseek-flash`**.
  Start the session with `deepseek-flash`, effort `high`. Do not escalate to a premium tier for this
  scope: it is additive C#, tests, and documentation, with no model calls.
- Entry message: paste everything between `BEGIN PROMPT` and `END PROMPT`.

```text
BEGIN PROMPT

# ROLE
You are implementing the Mac-side scaffold for real I2V (image-to-video) character animation in a
.NET 10 modular monolith. This is additive code + tests + docs. You will NOT run any model, GPU,
ComfyUI, Blender, or heavy FFmpeg work.

# READ FIRST (in this order, before touching anything)
1. ../AGENTS.md and AGENTS.md (repository + studio rules)
2. docs/agent/CHECKPOINT.md (resume point)
3. docs/context/STATE.md (verified state, "Known issues", and the Verification entry format)
4. docs/development/I2V_PLAN.md  <-- your checklist: P0.3, P1.1-P1.7
5. docs/development/DECISIONS.md (D-027 ComfyUI image, D-028 video clips, D-029 GPU gate,
   D-031 provider pattern, D-035 capability registry, D-036 recipes, D-045 identity pin)
6. docs/development/ARCHITECTURE.md (layer boundaries)

# ENVIRONMENT FACTS (already verified; do not re-benchmark)
- Mac M2, 8 GB, no GPU, no Ollama/ComfyUI. dotnet 10.0.401, ffmpeg 9.0.1, Docker available.
- Baseline on this machine: build 0 warnings/0 errors; `dotnet test` without a database is
  1168 total, 1157 pass, 10 fail (only `AIStudio.Tests.Persistence`, caused by the absent
  `ConnectionStrings__DefaultConnection`), 1 skipped (real audio E2E).

# SCOPE — do exactly these items, nothing more
P0.3 (only if Docker is permitted; otherwise record it as blocked) Database baseline:
  create `ai-studio/.env` with `POSTGRES_PORT=5544` (5432 is used by an unrelated container),
  `docker compose up -d`, then run the suite with
  `ConnectionStrings__DefaultConnection="Host=localhost;Port=5544;Database=aistudio;Username=postgres;Password=<local>"`.
  Target: 0 failed, 1 skipped.

P1.1 ADR in docs/development/DECISIONS.md: Application-owned `IVideoGenerationProvider` port,
  Infrastructure ComfyUI adapter, repository-owned trusted workflow only (no caller graph/nodes/paths),
  GPU lease held across submit -> poll -> download, CPU persist/probe after release, disabled by default.

P1.2 Application contracts in src/AIStudio.Application/Rendering/Visuals/ (model them on
  IImageGenerationProvider.cs and ImageGenerationRequest.cs):
  - `IVideoGenerationProvider.GenerateAsync(VideoGenerationRequest, CancellationToken)`
  - `VideoGenerationRequest`: animation prompt, optional first-frame reference (reuse the existing
    pinned identity type), duration seconds, frames per second, width, height, seed.
    No paths, URLs, workflow files, model names, or node graphs.
  - `VideoGenerationResult`: relative output path, SHA-256, byte size, duration, width/height/fps.
  - `VideoGenerationException` with stable codes: `video_request_invalid`,
    `video_generation_failed`, `video_timeout`, `video_artifact_invalid`, `video_not_configured`.

P1.3 Infrastructure options + DI:
  - `src/AIStudio.Infrastructure/Rendering/ComfyUiVideoOptions.cs` (`SectionName = "ComfyUiVideo"`,
    `Enabled=false` default, BaseUrl, WorkflowPath, Width/Height/Fps/MaxSeconds/TimeoutSeconds).
  - Register in src/AIStudio.Infrastructure/DependencyInjection.cs following the existing
    `AddHttpClient<IImageGenerationProvider, ComfyUiImageGenerationProvider>` pattern, with
    `Validate(...)` rules in the same style and `ValidateOnStart()`.
  - `src/AIStudio.Infrastructure/Rendering/ComfyUiVideoGenerationProvider.cs`: adapter skeleton.
    It may implement the HTTP submit/poll/download shape, but the workflow template may be a
    documented `TODO(verify-on-home)` placeholder. NEVER invent ComfyUI node names, model paths,
    or workflow JSON.

P1.4 Capability registration:
  - Add `CapabilityIds.VisualCharacterAnimation = new("visual.character_animation")` (this id is
    already the documented "valid data, no provider yet" example in D-036 — do not rename it).
  - Add it to `ProductionCapabilityCatalog.Capabilities` and add a `videoEnabled` parameter to
    `ProductionCapabilityCatalog.CreateProviders(...)`, mapped to the provider id `comfyui-i2v`.
  - Update every call site: `DependencyInjection.AddCapabilityRegistry` and
    `tests/AIStudio.Tests/Capabilities/ProductionCapabilityCatalogTests.cs`.
  - Note: `ProductionRecipeResolverTests.FutureCapabilityIsValidDataButUnresolved` builds its OWN
    local registry, so catalog registration does not break it. Do not edit that test.

P1.5 Tests (no network, no GPU):
  - `tests/AIStudio.Tests/Rendering/ComfyUiVideoGenerationProviderTests.cs` with a fake
    `HttpMessageHandler` (see ComfyUiImageGenerationProviderTests.cs): disabled provider throws
    `video_not_configured`; oversized/invalid request rejected; timeout maps to `video_timeout`;
    non-2xx/execution error maps to `video_generation_failed`; the request body contains no
    filesystem path or host-specific detail.
  - A reusable `FakeVideoGenerationProvider` test double for later wiring tests.
  - Update the catalog tests: new capability registered; provider disabled by default; enabled
    only when `ComfyUiVideo:Enabled=true`.

P1.6/P1.7 (documentation only): the real workflow JSON and the benchmark spec stay in
  docs/development/I2V_PLAN.md. If you cannot verify node names from local evidence, ship a
  skeleton plus `TODO(verify-on-home)` and say so explicitly in the report.

# HARD RULES
- No model inference, no ComfyUI/Blender/VoxCPM/ACE-Step calls, no GPU, no heavy FFmpeg render.
- Application must not reference Infrastructure (`ArchitectureBoundaryTests` enforces this).
- Additive only: do NOT change `SceneVisualRouter`, `GenerateSceneVisualsJobHandler`,
  `RenderVideoJobHandler`, or any production routing. The new provider stays unselected.
- Do NOT edit `FfmpegCommandPlan.cs`, `FfmpegVideoRenderer.cs`, `RenderingOptions.cs`,
  `SceneMediaInput.cs` — the unpushed home Ken Burns v2 work owns those files.
- No new NuGet packages, no schema/migration, no new endpoint, no commit to `main`.
- Branch: `feat/i2v-scaffold` from the current `docs/video1-state-cleanup` state (which already
  contains the I2V plan). Keep commits scoped.

# VERIFY AND REPORT EXACT OUTPUT
- `dotnet build AIStudio.slnx -c Release` -> expect 0 warnings / 0 errors
- `dotnet test AIStudio.slnx -c Release` -> report total / pass / fail / skipped. Without a database,
  only the 10 DB-gated `Persistence` tests may fail and 1 audio E2E may skip.
- `dotnet format AIStudio.slnx --verify-no-changes`
- `git diff --check`

# DOCS TO UPDATE IN THE SAME BRANCH
- `DECISIONS.md`: one new ADR entry for the I2V boundary (status: Implemented (scaffold)).
- `STATE.md`: one Verification entry in the existing format,
  `PASS (office code-only, <date>) - I2V scaffold v1: <commands> -> <exact counts>`.
- `CHECKPOINT.md`: update the resume line.
- `I2V_PLAN.md`: tick P1.1-P1.5 only for items you actually verified.
- `REPO_MAP.md`: add a short "Video generation (I2V)" section listing the new files.

# REPORT FORMAT
1. Summary (5-10 lines): done / deliberately deferred.
2. Branch + commits (hash + message).
3. Files created/changed (relative paths).
4. Verification table with the exact command output numbers.
5. Blockers and decisions waiting on the human.
6. Handoff for the home machine (what P2 needs).

# STOP AND ASK
If you would need to change routing/handlers, add a package, touch the DB schema, or a test fails
for a reason unrelated to your change.

END PROMPT
```
