# Agent Checkpoint

Updated: 2026-09-17

- Current Phase: P1.
- AI Infrastructure Foundation v1: DONE / RUNTIME VALIDATED / FROZEN.
- API Surface: `POST /api/content-projects`, `POST /api/content-projects/{contentProjectId}/generate-idea-jobs`, and `GET /api/jobs/{jobId}`.
- Validated Flow: ContentProject -> GenerateIdea durable PostgreSQL Job -> Worker -> local Ollama -> structured `Job.Result` -> HTTP retrieval. The API returned `completed` with all six result fields and no worker/lease internals; PostgreSQL confirmed `Succeeded` with a persisted result.
- Runtime Compatibility: `qwen3.6:27b-q4_K_M` consistently failed JSON syntax validation with `ai_malformed_response`; the earlier coding-model smoke failed identically. `qwen3.8:27b-q4_K_M` completed through the existing retry policy after one timeout and one malformed response, with no application change.
- Phase 0 Local Developer AI: Ollama `0.34.1` is active as the WSL2 system service at `http://127.0.0.1:11434`; `OLLAMA_CONTEXT_LENGTH=49152` is verified. Qwen Code is currently `0.24.0` (the prior checkpoint recorded `0.23.4`) and uses `Qwen Code -> Anthropic-compatible provider -> Ollama`.
- Model Roles: developer coding model is `qwen3.6:27b-coding`; validated Content Studio runtime model is `qwen3.8:27b-q4_K_M`. The development configuration now explicitly requests `qwen3.8:27b-q4_K_M`; environment-variable model overrides remain supported.
- Qwen Routing: developer-productivity only. Use Qwen for bounded, repetitive, localized, low-risk work; use Codex for architecture, security/authorization, concurrency, migrations, durable jobs, destructive operations, ambiguous cross-cutting work, and complex debugging. Human diff review is mandatory before committing Qwen changes; do not build a compatibility platform around Qwen/Ollama.
- Qwen Patch Review: APPROVED and committed as `f24f770`. `Serialize()` validates and serializes the same trimmed canonical values for all six fields; `GenerateIdeaContractsTests` passed 8/8. Future Qwen patches still require human/Codex review.
- Qwen Code Observation: direct local coding-model inference is usable; substantial delays were observed around compaction/tool orchestration. Qwen Code `0.24.0` clears its file-read cache after successful auto-compaction, which could require a later re-read, but the old session lacks a trace proving that this caused the observed Edit failure.
- Next Recommended Step: select the next Content Studio product milestone under separate instruction. GenerateScript remains separate and deferred.
