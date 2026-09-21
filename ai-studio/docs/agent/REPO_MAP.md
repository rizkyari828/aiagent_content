# Focused Repository Map

Use this for navigation, then inspect current code.

Content Studio now lives under `ai-studio/`. Unless a path starts with `../`, paths below are relative to the `ai-studio/` directory.

## GenerateIdea

- `src/AIStudio.Application/Jobs/GenerateIdea/GenerateIdeaWorkflow.cs` — enqueue workflow.
- `GenerateIdeaJobHandler.cs`, `GenerateIdeaJobPayload.cs`, `GenerateIdeaPrompt.cs`, `GenerateIdeaResult.cs` — execution contract, prompt, and structured result.

## GenerateScript

- `src/AIStudio.Application/Jobs/GenerateScript/GenerateScriptWorkflow.cs` — enqueue selected idea as a durable script job.
- `GenerateScriptJobHandler.cs`, `GenerateScriptJobPayload.cs`, `GenerateScriptPrompt.cs`, `GenerateScriptResult.cs` — execution contract, prompt, and structured script result.

## GenerateStoryboard

- `src/AIStudio.Application/Jobs/GenerateStoryboard/GenerateStoryboardWorkflow.cs` — enqueue from an approved reviewed script.
- `GenerateStoryboardJobHandler.cs`, `GenerateStoryboardJobPayload.cs`, `GenerateStoryboardPrompt.cs`, `GenerateStoryboardResult.cs`, `StoryboardGenerationException.cs` — execution contract, prompt, structured result, and gate errors.
- `src/AIStudio.Api/Endpoints/StoryboardEndpoints.cs` — minimal storyboard enqueue HTTP surface.

## Assets (Video #1 collection)

- `src/AIStudio.Domain/Assets/SceneAsset.cs` — canonical per-scene asset metadata with provenance/license invariants.
- `src/AIStudio.Application/Assets/AssetCollectionWorkflow.cs` — register a local file against a completed GenerateStoryboard result and list project assets.
- `src/AIStudio.Infrastructure/Assets/LocalAssetFileStore.cs` — approved-root, traversal- and symlink-safe file validation plus SHA-256 integrity hashing.
- `src/AIStudio.Infrastructure/Assets/AssetRepository.cs`, `AssetStorageOptions.cs` — persistence and configured asset root.
- `src/AIStudio.Infrastructure/Persistence/Configurations/SceneAssetConfiguration.cs` — `scene_assets` mapping and unique project/scene index.
- `src/AIStudio.Api/Endpoints/AssetEndpoints.cs` — minimal asset registration and listing HTTP surface.

## Narration (Video #1 narration)

- `src/AIStudio.Domain/Narration/NarrationTrack.cs` — one canonical narration track per project with provenance/license invariants.
- `src/AIStudio.Application/Narration/NarrationWorkflow.cs` — register a local narration file against a completed GenerateStoryboard result and retrieve it.
- `src/AIStudio.Infrastructure/Narration/NarrationRepository.cs` — persistence.
- `src/AIStudio.Infrastructure/Persistence/Configurations/NarrationTrackConfiguration.cs` — `narration_tracks` mapping and unique project index.
- `src/AIStudio.Api/Endpoints/NarrationEndpoints.cs` — minimal narration registration and retrieval HTTP surface.

## Subtitles (Video #1 subtitle track)

- `src/AIStudio.Domain/Subtitles/SubtitleTrack.cs` — one canonical `.srt` subtitle track per project with provenance/license invariants.
- `src/AIStudio.Application/Subtitles/SubtitleWorkflow.cs` — register an operator-provided `.srt` against a completed GenerateStoryboard result and retrieve it.
- `src/AIStudio.Infrastructure/Subtitles/SubtitleRepository.cs`, `Persistence/Configurations/SubtitleTrackConfiguration.cs` — persistence and `subtitle_tracks` mapping.
- `src/AIStudio.Api/Endpoints/SubtitleEndpoints.cs` — minimal subtitle registration and retrieval HTTP surface.

## Rendering (FFmpeg composition)

- `src/AIStudio.Application/Jobs/RenderVideo/RenderVideoWorkflow.cs` — enqueue a durable render job from storyboard + assets + narration gating.
- `RenderVideoJobHandler.cs`, `RenderVideoJobPayload.cs`, `RenderVideoResult.cs` — execution contract, input integrity gates, and canonical `Job.Result` render evidence.
- `src/AIStudio.Application/Rendering/IVideoRenderer.cs`, `VideoRenderRequest.cs`, `VideoRenderOutput.cs`, `SceneMediaInput.cs`, `RenderVideoException.cs` — renderer process boundary and DTOs.
- `src/AIStudio.Application/Rendering/SceneMotion.cs`, `SceneTransition.cs`, `SubtitleStyle.cs`, `BackgroundMusic.cs`, `SceneSoundEffect.cs`, `VideoRenderSettings.cs` — Video Quality v1 motion/transition/subtitle/audio settings (deterministic defaults; per-scene overrides optional).
- `src/AIStudio.Application/Rendering/Visuals/SceneVisualBrief.cs`, `SceneVisualSvg.cs`, `ISceneVisualRenderer.cs` — Visual Asset Engine: structured brief, pure SVG composer (Hero/Cards/Window/Chat), and the rasterizer boundary.
- `src/AIStudio.Infrastructure/Rendering/FfmpegSceneVisualRenderer.cs` — rasterizes a `SceneVisualBrief` to PNG via `IProcessRunner` + FFmpeg librsvg.
- `src/AIStudio.Application/Rendering/IMediaInspector.cs`, `src/AIStudio.Infrastructure/Rendering/FfprobeMediaInspector.cs` — shared ffprobe stream/duration/dimension inspection behind `IProcessRunner`.
- `src/AIStudio.Application/Rendering/SceneTiming.cs`, `SubtitleTimeline.cs` — narration-aware per-scene timing (text weights + 2.0s static / 3.5s animated floors; single source for render and generated visuals) and derived subtitle cue serialization + canonical cue reading.
- `src/AIStudio.Infrastructure/Rendering/FfmpegVideoRenderer.cs` — safe `ArgumentList` FFmpeg invocation, bounded timeout, temp-file + atomic promotion, background-music path resolution.
- `FfmpegCommandPlan.cs`, `RenderingOptions.cs` — pure argument/filter/timing plan (motion, transitions, styled subtitle burn-in, music/ducking/SFX audio graph) and rendering configuration.
- `src/AIStudio.Api/Endpoints/RenderEndpoints.cs` — minimal render enqueue HTTP surface.

## Audio Mixing / Mastering

- `src/AIStudio.Application/Rendering/AudioMixing/IAudioMixer.cs`, `AudioMixRequest.cs`, `AudioMixOutput.cs`, `AudioMixException.cs` — standalone mastering contract: approved asset references (narration required, music optional) -> relative `.wav` output plus hash/size/duration/format; structured `audio_*` errors.
- `src/AIStudio.Infrastructure/Rendering/AudioMixing/FfmpegAudioMixer.cs` — CPU-only FFmpeg mixer behind `IProcessRunner`/`IMediaInspector`, resolving inputs through `IAssetFileStore`; never acquires `IGpuResourceGate`.
- `src/AIStudio.Infrastructure/Rendering/AudioMixing/FfmpegAudioMixCommandPlan.cs`, `AudioMixingOptions.cs` — safe argument/filter plan (narration/music `loudnorm`, sidechain ducking, fades, `alimiter`, 48 kHz stereo) with every value centralized in options.
- `tests/AIStudio.Tests/Rendering/FfmpegAudioMixCommandPlanTests.cs`, `FfmpegAudioMixerTests.cs`, `FfmpegAudioMixerIntegrationTests.cs` — argument safety/format/ducking/fade/limiter, missing input, FFmpeg failure, cancellation, space-containing paths, path traversal, and a real-FFmpeg smoke test (skips without ffmpeg).

## Audio Generation (local providers)

- `src/AIStudio.Application/Rendering/AudioGeneration/ISpeechSynthesisProvider.cs`, `SpeechSynthesisRequest.cs`, `SpeechSynthesisResult.cs`, `SpeechVoiceProfile.cs`, `SpeechSynthesisException.cs` — local TTS boundary: curated voice presets, text data only, approved asset output, `speech_*` errors.
- `src/AIStudio.Application/Rendering/AudioGeneration/IMusicGenerationProvider.cs`, `MusicGenerationRequest.cs`, `MusicGenerationResult.cs`, `MusicGenerationException.cs` — local music boundary: caption/duration/bpm/instrumental/seed, approved asset output, `music_*` errors.
- `src/AIStudio.Infrastructure/Rendering/AudioGeneration/VoxCpmSpeechSynthesisProvider.cs`, `SpeechSynthesisOptions.cs` — launches repository-owned `Rendering/VoxCPM2/synthesize.py` with trusted python/model paths plus one JSON request, persists via `IAssetFileStore`, verifies via `IMediaInspector`, holds `IGpuResourceGate` across the GPU process only; `SpeechSynthesis:VoxCPM2:*`, disabled by default.
- `src/AIStudio.Infrastructure/Rendering/AudioGeneration/AceStepMusicGenerationProvider.cs`, `MusicGenerationOptions.cs` — launches repository-owned `Rendering/ACE-Step/generate.py` (`AceStepHandler`/`generate_music`, `acestep-v15-turbo`) with trusted python/project/model paths plus one JSON request; same gate/persist/probe rules; `MusicGeneration:AceStep:*`, disabled by default.
- `src/AIStudio.Infrastructure/Rendering/VoxCPM2/synthesize.py`, `src/AIStudio.Infrastructure/Rendering/ACE-Step/generate.py` — repository-owned launchers: read one constrained JSON request and write one result JSON; no request-supplied code, paths, or CLI flags.
- `tests/AIStudio.Tests/Rendering/VoxCpmSpeechSynthesisProviderTests.cs`, `AceStepMusicGenerationProviderTests.cs`, `AudioGenerationTestSupport.cs`, `AudioGenerationEndToEndTests.cs` — voice/seed mapping, validation, error codes, GPU-gate lifecycle, shell-safety, and a real-runtime E2E gated by `AISTUDIO_VOXCPM_*` / `AISTUDIO_ACESTEP_*` env vars.

## Production Orchestration v2 (durable audio + mastered render)

- `src/AIStudio.Application/Jobs/GenerateAudio/GenerateAudioWorkflow.cs`, `GenerateAudioJobHandler.cs`, `GenerateAudioJobPayload.cs`, `GenerateAudioResult.cs` — one durable job with three retry-safe stages (narration, BGM, mastered mix); narration text derived from the storyboard title + scene headings; maps `audio_narration_failed` / `audio_music_failed` / `audio_mix_failed` / `audio_artifact_invalid`; holds no GPU lease.
- `src/AIStudio.Application/Rendering/AudioProduction/AudioProductionWorkspace.cs`, `AudioProductionManifest.cs`, `AudioProductionFingerprint.cs`, `AudioProductionException.cs` — deterministic `audio/{projectId}/{storyboardJobId}/` layout plus a narrow `manifest.json` fingerprint/artifact reuse mechanism (validates fingerprint, SHA-256, byte size and probed audio/format); shared by GenerateAudio and the renderer.
- `src/AIStudio.Api/Endpoints/AudioEndpoints.cs` — `POST /api/content-projects/{id}/storyboard-jobs/{storyboardJobId}/audio-jobs`.
- `src/AIStudio.Application/Jobs/RenderVideo/RenderVideoResult.cs`, `RenderVideoJobHandler.cs`, `RenderVideoWorkflow.cs` — prefer the validated mastered audio (`audioSource=mastered`, `masteredAudioPath`/`masteredAudioContentHash`), fall back to the legacy `narration_tracks` row; scene timing, transitions and subtitles unchanged.
- `tests/AIStudio.Tests/Rendering/GenerateAudioJobHandlerTests.cs`, `GenerateAudioWorkflowTests.cs`, `GenerateAudioContractsTests.cs`, `GenerateAudioTestDoubles.cs` — staged generation, full/partial reuse, only-failed-stage retry, input-change invalidation, disabled providers, cancellation, no GPU-gate constructor, and master-consumption render tests.

## Capability Registry

- `src/AIStudio.Application/Capabilities/CapabilityId.cs`, `CapabilityIds.cs` — validated dotted capability value object plus the v1 identifier set (stable strings, not an enum).
- `CapabilityDescriptor.cs`, `CapabilityProviderDescriptor.cs`, `CapabilityAvailability.cs` — capability metadata and trusted provider registration (id, capability, priority, enabled, runtime category; REGISTERED vs ENABLED, no executable/model paths).
- `CapabilityRequirement.cs`, `CapabilityResolution.cs`, `CapabilityGap.cs`, `ICapabilityRegistry.cs` — requirement with ordered fallback capability chain, resolution result, explicit gap, and the application-facing registry boundary.
- `CapabilityRegistry.cs`, `ProductionCapabilityCatalog.cs` — pure deterministic registry (fail-fast duplicate/unknown registration, priority ordering, fallback resolution) and the compile-time mapping of the current provider stack.
- `tests/AIStudio.Tests/Capabilities/` — id validation/stability, registration and duplicate validation, deterministic resolution, disabled/missing capability gaps, fallback selection, catalog mapping, and DI enablement reflection.

## Production Recipes

- `src/AIStudio.Application/ProductionRecipes/ProductionRecipeId.cs`, `ProductionRecipeVersion.cs` — validated data identity (stable id + positive version), so a new content format needs no new type or enum.
- `ProductionRecipe.cs`, `ProductionRecipeRequirement.cs` — declarative recipe (id + version + requirements) and a required/optional wrapper around the capability-registry `CapabilityRequirement`; data only, no provider/path/script fields.
- `ProductionRecipeIssue.cs`, `ProductionRecipeValidator.cs` — deterministic structural validation and stable issue codes (invalid id/version, empty or required-less requirements, duplicate primary, fallback hygiene); never consults the capability registry.
- `IProductionRecipeRegistry.cs`, `ProductionRecipeRegistry.cs` — in-memory catalog keyed by id + version with fail-fast duplicate/invalid rejection and `Get`/`GetLatest`.
- `IProductionRecipeResolver.cs`, `ProductionRecipeResolver.cs`, `ProductionRecipeResolution.cs` — execution-free resolution delegating each requirement to `ICapabilityRegistry.Resolve`; exposes `FullySupported` / `SupportedWithFallbacks` / `Unsupported`, selected providers, fallbacks, and gaps.
- `SeedProductionRecipes.cs` — the small data catalog: `tech-explainer` v1 and `motion-comic` v1.
- `tests/AIStudio.Tests/ProductionRecipes/` — id/version validation, structural validation, registry registration/duplicate/list/get/latest, deterministic resolution and statuses, required-vs-optional, provider-disabled and missing-capability gaps, future-capability-as-data, delegation/inheritance from the capability registry, JSON round-trip, seed shape, and DI wiring.

## Concepts

- `src/AIStudio.Application/Concepts/ConceptId.cs`, `ConceptVersion.cs`, `ConceptIdentifier.cs` — validated data identity for a concept and the lowercase token rule for format/style/tag values (data, not enums).
- `ConceptManifest.cs`, `ConceptValidationIssue.cs`, `ConceptValidator.cs` — WHAT to make (descriptive metadata + a trusted recipe reference) and narrow structural validation that never scores quality or consults the registries.
- `IConceptRegistry.cs`, `ConceptRegistry.cs` — trusted in-memory boundary: validate-on-register, fail-fast duplicate id+version, deterministic id-then-version order, `Get`/`GetLatest`.
- `IConceptResolver.cs`, `ConceptResolver.cs`, `ConceptResolution.cs` — execution-free resolution mapping the concept to a recipe version (exact or latest) and delegating to `IProductionRecipeResolver`; `Ready` / `ReadyWithFallbacks` / `Unsupported`, with `recipe_not_found` kept distinct from `recipe_unsupported`.
- `ConceptJsonConverters.cs` — clean scalar JSON for `ConceptId`/`ConceptVersion`; recipe id/version converters live in `ProductionRecipes/ProductionRecipeJsonConverters.cs`.
- `SeedConcepts.cs` — the small data catalog: `local-ai-tech-explainer` v1 and `local-ai-motion-comic` v1.
- `tests/AIStudio.Tests/Concepts/` — id/version/token validation, structural validation, registry behavior, resolver statuses and delegation, missing-recipe vs capability-gap, latest resolution, JSON round-trip, new-format-without-new-C#, seed shape, and DI wiring.

## Creative Director (planning above Concept Registry)

- `src/AIStudio.Application/Creative/ApprovedIdea.cs`, `CreativeConstraints.cs`, `ApprovedIdeaValidator.cs` — the approved-idea boundary the director consumes, its optional user hints, and structural validation; `ApprovedIdea.FromGenerateIdeaResult(...)` maps the existing `GenerateIdeaResult` without duplicating idea generation.
- `CreativeTreatment.cs`, `CreativeTreatmentValidator.cs` — HOW the idea feels and flows (story approach, hook, pacing, visual strategy, ending, optional tone/transition) as validated descriptive data; no script or storyboard.
- `CreativeDirection.cs`, `CreativeDirectionParser.cs`, `CreativeDirectionException.cs` — the output (`ConceptManifest` + `CreativeTreatment`), strict untrusted-JSON parsing that reuses `ConceptValidator`, and stable error codes.
- `CreativePlanningContext.cs`, `CreativePlanningContextProvider.cs` — the SAFE recipe/capability summary (recipe ids, capability ids, resolvable/fallback flags; no providers/paths) built from the trusted registries.
- `CreativeDirectorPrompt.cs` — deterministic, token-efficient prompt builder with the approved idea, safe catalog, JSON contract, and creative boundaries.
- `ICreativeDirector.cs`, `CreativeDirector.cs`, `CreativeDirectionOptions.cs`, `CreativeDirectionResult.cs` — orchestration over the existing `IAiTextGenerator`, structural validation, and the guardrail that preserves the approved audience/reference.
- `tests/AIStudio.Tests/Creative/` — idea validation + mapping, prompt content/safety/determinism, parser accept/reject, planning-context safety, director orchestration/guardrails/resolution, critical Solution-Idea-preservation regression, and DI wiring.

## Narrative Sync + Scene Timing

- `src/AIStudio.Application/Rendering/Narration/ReviewedNarrationScript.cs`, `NarrationText.cs`, `NarrativeTiming.cs` - parse the approved reviewed script, map it to storyboard scenes (positional/heading, else `audio_narration_source_unmapped`), strip quotes for TTS-safe text, and centralize lead/hold/transition constants.
- `src/AIStudio.Application/Jobs/GenerateAudio/GenerateAudioJobHandler.cs`, `GenerateAudioResult.cs` - per-scene VoxCPM2 narration, CPU assembly, BGM/master, and per-scene narration+timing evidence.
- `src/AIStudio.Application/Rendering/AudioProduction/AudioProductionManifest.cs`, `AudioProductionWorkspace.cs`, `IAudioNarrationAssembler.cs`, `src/AIStudio.Infrastructure/Rendering/AudioProduction/FfmpegNarrationAssembler.cs` - manifest v2 with per-scene narration/timing; deterministic `adelay`+`amix` narration assembly.
- `SubtitleTimeline.cs` (`BuildFromNarration`), `RenderVideoJobHandler.cs`, `FfmpegCommandPlan.cs` (`ResolveContentDurations`), `SceneVisualDirector.cs` (`SceneChoreographyPlanner`) - phrase subtitles and beats constrained to each scene's speech window, explicit speech-driven scene durations into the renderer.

## Visual Direction + Multi-Engine Choreography

- `src/AIStudio.Application/Rendering/Visuals/SceneVisualIntent.cs`, `SceneVisualDirection.cs`, `AnimationPrimitive.cs`, `AnimationBeat.cs` - structured intent/composition, per-scene visual direction, curated animation primitives and choreography beats (data only; no executable content).
- `src/AIStudio.Application/Rendering/Visuals/SceneVisualDirector.cs`, `SceneVisualRouter.cs`, `SceneVisualFingerprint.cs` - deterministic keyword director, engine router with the documented availability fallback chain, and per-scene plan fingerprint used for reuse/invalidation.
- `src/AIStudio.Application/Rendering/Visuals/ChoreographyEvaluator.cs`, `SceneVisualSvg.cs` (`ComposeFrame`/`ComposeOverlay`), `ISceneVisualRenderer.cs` - bounded frame-state evaluation and trusted SVG frame composition; `FfmpegSceneVisualRenderer` gained `RenderAnimationAsync` (AnimatedSvg frame sequence) and `RenderImageMotionAsync` (FLUX slow push + animated overlay).
- `src/AIStudio.Infrastructure/Rendering/Manim/templates.py` (`ProcessFlow`), `render_scene.py` - trusted process animation (command/progress/steps/completion) reused for download/install and model-pull scenes.
- `tests/AIStudio.Tests/Rendering/SceneVisualRoutingTests.cs`, `GenerateSceneVisualsJobHandlerTests.cs`, `FfmpegSceneVisualRendererTests.cs`, `SceneVisualPlannerTests.cs` - intent classification, deterministic routing/fallback, choreography bounds, frame reveal, subtitle safe area, per-scene fingerprint reuse/invalidation, forced regeneration, and renderer command shape.

## Visual Asset Pipeline (durable planning + animation)

- `src/AIStudio.Application/Rendering/Visuals/SceneVisualPlanner.cs` (v2), `SceneVisualPlan.cs`, `SceneVisualEngine.cs`, `SceneAnimationTemplate.cs`, `SceneAnimationParameters.cs`, `SceneVisualEngineSelector.cs` — deterministic storyboard-to-visual mapping and engine/template selection. v2 resolves display text only from the heading + quoted strings + concept vocabulary and never renders raw `visual` instruction prose.
- `src/AIStudio.Application/Rendering/Visuals/IManimSceneRenderer.cs`, `src/AIStudio.Infrastructure/Rendering/ProcessManimSceneRenderer.cs`, `ManimOptions.cs`, `Rendering/Manim/render_scene.py`, `Rendering/Manim/templates.py` — Manim animation engine behind the shared process boundary with repository-owned templates.
- `src/AIStudio.Application/Rendering/Visuals/IImageGenerationProvider.cs`, `ImageGenerationRequest.cs`, `SceneImagePrompt.cs`, `src/AIStudio.Infrastructure/Rendering/ComfyUiImageGenerationProvider.cs`, `ComfyUiOptions.cs`, `Rendering/ComfyUI/flux2_klein_4b_distilled.json` — optional local ComfyUI AI image engine; submits one approved FLUX.2 Klein workflow, injects prompt/seed/dimensions only, maps timeout/error codes. Selected as `SceneVisualEngine.AiImage` for stills when `ComfyUi:Enabled=true`.
- `src/AIStudio.Application/Rendering/Visuals/IThreeDRenderingProvider.cs`, `ThreeDRenderRequest.cs`, `SceneThreeDTemplate.cs`, `src/AIStudio.Infrastructure/Rendering/Blender/BlenderThreeDRenderingProvider.cs`, `BlenderOptions.cs`, `Rendering/Blender/templates/local_ai_laptop.py` — optional local Blender 3D engine; renders one trusted template to a PNG sequence then encodes H.264 via the shared FFmpeg boundary, injecting structured palette/duration/seed only, maps `threed_*` error codes. Selected as `SceneVisualEngine.ThreeD` when `Blender:Enabled=true` and a laptop + local/offline scene is not already claimed by Manim.
- `src/AIStudio.Application/Rendering/IGpuResourceGate.cs`, `src/AIStudio.Infrastructure/Gpu/GpuResourceGate.cs`, `GpuResourceGateOptions.cs` — process-local exclusive GPU lease shared by ComfyUI, Blender, and Ollama; async-disposable lease, singleton DI, no priorities/VRAM awareness/cross-process locking.
- `src/AIStudio.Application/Jobs/GenerateSceneVisuals/` — durable `GenerateSceneVisuals` workflow, handler, payload, and result (auto-generates and registers `SceneAsset` rows idempotently).
- `src/AIStudio.Application/Assets/SceneAssetProvenance.cs` — generated-visual source marker; `IAssetFileStore.WriteAsync`/`LocalAssetFileStore` — path-safe idempotent writing of generated bytes.
- `src/AIStudio.Api/Endpoints/VisualEndpoints.cs` — minimal visual enqueue HTTP surface.

## Final Video QA (pre-publish deterministic validation)

- `src/AIStudio.Application/Jobs/FinalVideoQa/FinalVideoQaJobHandler.cs` — re-reads the RenderVideo artifact through `IAssetFileStore`, verifies its SHA-256, probes it, and returns pass-only `FinalVideoQaResult` or a stable `qa_*` error code.
- `FinalVideoQaResult.cs`, `FinalVideoQaJobPayload.cs`, `FinalVideoQaWorkflow.cs`, `FinalVideoQaException.cs` — canonical `Job.Result` evidence, enqueue contract/gating, and enqueue errors.
- `src/AIStudio.Api/Endpoints/FinalVideoQaEndpoints.cs` — minimal QA enqueue HTTP surface.

## Manual Script Review/Edit

- `src/AIStudio.Domain/Scripts/ReviewedScript.cs` — one canonical reviewed script per project with `Draft -> Approved` lifecycle.
- `src/AIStudio.Application/Scripts/ScriptReviewWorkflow.cs` — initialize from completed GenerateScript evidence, edit, retrieve, and approve.
- `src/AIStudio.Api/Endpoints/ScriptReviewEndpoints.cs` — minimal review HTTP surface.
- `src/AIStudio.Infrastructure/Persistence/Configurations/ReviewedScriptConfiguration.cs` — JSONB state, provenance foreign key, and uniqueness constraints.

## Durable jobs and worker

- `src/AIStudio.Domain/Jobs/Job.cs`, `JobStatus.cs`, `JobType.cs` — durable job domain.
- `src/AIStudio.Infrastructure/Jobs/PostgreSqlJobQueue.cs` — PostgreSQL queue authority.
- `JobWorker.cs`, `JobProcessor.cs`, `JobReader.cs` — worker execution and retrieval.

## HTTP API

- `src/AIStudio.Api/Endpoints/GenerateIdeaEndpoints.cs` — GenerateIdea/GenerateScript enqueue and shared job retrieval endpoints.

## AI boundary/provider

- `src/AIStudio.Application/AI/IAiTextGenerator.cs`, `AiTextRequest.cs`, `AiTextResponse.cs` — application boundary.
- `src/AIStudio.Infrastructure/AI/OllamaTextGenerator.cs`, `OllamaOptions.cs` — local Ollama provider.

## Tests

- `tests/AIStudio.Tests/Jobs/GenerateIdeaContractsTests.cs` — payload/result contracts.
- `GenerateIdeaWorkflowTests.cs`, `GenerateIdeaJobHandlerTests.cs`, `JobWorkerTests.cs` — GenerateIdea and shared job behavior.
- `GenerateScriptContractsTests.cs`, `GenerateScriptWorkflowTests.cs`, `GenerateScriptJobHandlerTests.cs`, `GenerateScriptApiContractTests.cs` — focused script behavior; `Persistence/GenerateScriptVerticalSliceTests.cs` covers the optional PostgreSQL path.

- `tests/AIStudio.Tests/Jobs/GenerateStoryboardContractsTests.cs`, `GenerateStoryboardJobHandlerTests.cs`, `GenerateStoryboardWorkflowTests.cs`, `GenerateStoryboardApiContractTests.cs` — approved/draft/missing script gating, ordering, validation, canonical `Job.Result`, and retrieval.
- `tests/AIStudio.Tests/Scripts/` — lifecycle, canonical edit, idempotent approval, and API response contracts.
- `tests/AIStudio.Tests/Persistence/ReviewedScriptModelTests.cs` — additive EF mapping and uniqueness validation.
- `tests/AIStudio.Tests/Assets/` — scene asset domain rules, workflow association/provenance, path-safety and hashing, and API response contracts; `Persistence/SceneAssetModelTests.cs` and DB-gated `Persistence/SceneAssetVerticalSliceTests.cs` cover mapping and persistence.
- `tests/AIStudio.Tests/Narration/` — narration domain rules, storyboard-gated workflow, provenance, shared path-safety/hash reuse, and API response contracts; `Persistence/NarrationTrackModelTests.cs` and DB-gated `Persistence/NarrationVerticalSliceTests.cs` cover mapping and persistence.
- `tests/AIStudio.Tests/Subtitles/` — subtitle domain rules, `.srt`-only storyboard-gated workflow, provenance, path safety, and API response contracts; `Persistence/SubtitleTrackModelTests.cs` covers `subtitle_tracks` mapping. Renderer subtitle embedding is covered in `Rendering/`.
- `tests/AIStudio.Tests/Rendering/` — FFmpeg argument/timing plan, render enqueue gating, handler input-integrity gates, safe output paths, faked process boundary, `Job.Result` render-evidence contracts, transition ghosting policy, visual planner + engine selection, Manim process/template-safety boundary, durable visual generation (idempotency, backward compatibility), Final Video QA handler/workflow/contracts plus `FfprobeMediaInspector` parsing/failure tests (no installed FFmpeg required).
- `tests/AIStudio.Tests/Rendering/SceneVisualPlannerTests.cs`, `SceneTimingTests.cs`, `FfmpegVideoRendererTests.cs`, `RenderVideoJobHandlerTests.cs`, `GenerateSceneVisualsJobHandlerTests.cs`, `ComfyUiImageGenerationProviderTests.cs`, `SceneImagePromptTests.cs`, `BlenderThreeDRenderingProviderTests.cs`, `GpuResourceGateTests.cs`, `GpuResourceGateDependencyInjectionTests.cs` — planner display-text/layout mapping, timing allocation + `SubtitleTimeline`, durable Manim duration from narration, durable subtitle re-timing, optional AI image engine selection/prompt/ComfyUI error mapping, optional Blender 3D selection/trusted-template injection/`threed_*` error mapping, and GPU-gate exclusion/cancellation/idempotency/shared-DI behavior.

## Product specification (modular PRD v0.9.2)

- `docs/product/PRD_INDEX.md` — split index and task-to-document map; read this first.
- `docs/product/parts/01..12_*.md` — canonical topical split of every numbered v0.9.2 section.
- `docs/product/execution-context/STORYBOARD.md`, `NARRATION_TTS.md`, `ASSETS.md`, `RENDERING_QA.md` — narrow Video #1 implementation contexts.
- `docs/product/SECTION_MAP.md` — where each source section moved.
- `docs/product/_source/Local_AI_Ecosystem_PRD_v0.9.2.md` — frozen original, authoritative on divergence; read only to resolve ambiguity.

## Agent context

- `../AGENTS.md` (Repository Context Protocol), `AGENTS.md` (Studio rules), `QWEN.md`, `docs/agent/CHECKPOINT.md`, `docs/context/STATE.md`, `docs/agent/REPO_MAP.md`, `docs/development/DECISIONS.md`, `docs/agent/LESSONS.md` — monorepo + Studio rules, resume point, verified state, module map, and durable decision rationale.
