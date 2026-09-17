# Work state

Updated: 2026-09-17.

## Current milestone

The validated Content Studio development runtime model is now explicit. GenerateIdea remains the completed first AI vertical slice; GenerateScript and later workflows remain deferred.

## Implemented

- `Ollama:DefaultModel`, its options fallback, and `.env.example` now use `qwen3.8:27b-q4_K_M`.
- `Ollama__DefaultModel` and the existing optional per-job model override remain available.
- Content Studio model selection remains application configuration; Ollama context/GPU tuning remains a separate local-ai-infra concern.
- The developer coding model remains `qwen3.6:27b-coding`.
- GenerateIdea persists validated structured output in `Job.Result`; durable-job ownership, retry, lease, schema, and API behavior are unchanged.

## Verification

- PASS - 12/12 focused Ollama generator and GenerateIdea DI tests.
- PASS - Release API build with 0 warnings/errors on .NET SDK 10.0.401.
- PASS - application model identity matches `local-ai-infra/profiles/studio.yaml`.
- PASS - JSON syntax and whitespace checks.
- NOT RUN - another live 27B smoke; the same model already completed the real GenerateIdea E2E flow.

## Known issues and next task

Select the next Content Studio product milestone under separate instruction. GenerateScript remains explicitly out of scope for this configuration milestone.
