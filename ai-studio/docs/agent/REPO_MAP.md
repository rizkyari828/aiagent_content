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

## Product specification (modular PRD v0.9.2)

- `docs/product/PRD_INDEX.md` — split index and task-to-document map; read this first.
- `docs/product/parts/01..12_*.md` — canonical topical split of every numbered v0.9.2 section.
- `docs/product/execution-context/STORYBOARD.md`, `NARRATION_TTS.md`, `ASSETS.md`, `RENDERING_QA.md` — narrow Video #1 implementation contexts.
- `docs/product/SECTION_MAP.md` — where each source section moved.
- `docs/product/_source/Local_AI_Ecosystem_PRD_v0.9.2.md` — frozen original, authoritative on divergence; read only to resolve ambiguity.

## Agent context

- `../AGENTS.md`, `AGENTS.md`, `QWEN.md`, `docs/agent/CHECKPOINT.md`, `docs/agent/LESSONS.md` — monorepo rules, Studio rules, current state, and validated knowledge.
