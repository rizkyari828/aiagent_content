# Focused Repository Map

Use this for navigation, then inspect current code.

Content Studio now lives under `ai-studio/`. Unless a path starts with `../`, paths below are relative to the `ai-studio/` directory.

## GenerateIdea

- `src/AIStudio.Application/Jobs/GenerateIdea/GenerateIdeaWorkflow.cs` — enqueue workflow.
- `GenerateIdeaJobHandler.cs`, `GenerateIdeaJobPayload.cs`, `GenerateIdeaPrompt.cs`, `GenerateIdeaResult.cs` — execution contract, prompt, and structured result.

## GenerateScript

- `src/AIStudio.Application/Jobs/GenerateScript/GenerateScriptWorkflow.cs` — enqueue selected idea as a durable script job.
- `GenerateScriptJobHandler.cs`, `GenerateScriptJobPayload.cs`, `GenerateScriptPrompt.cs`, `GenerateScriptResult.cs` — execution contract, prompt, and structured script result.

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

- `tests/AIStudio.Tests/Scripts/` — lifecycle, canonical edit, idempotent approval, and API response contracts.
- `tests/AIStudio.Tests/Persistence/ReviewedScriptModelTests.cs` — additive EF mapping and uniqueness validation.

## Agent context

- `../AGENTS.md`, `AGENTS.md`, `QWEN.md`, `docs/agent/CHECKPOINT.md`, `docs/agent/LESSONS.md` — monorepo rules, Studio rules, current state, and validated knowledge.
