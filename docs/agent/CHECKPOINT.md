# Agent Checkpoint

Updated: 2026-09-16

- Current Phase: P1.
- Current Milestone: GenerateIdea HTTP API vertical slice and Codex/Qwen repository handoff guidance are implemented; no application, schema, or durable-job semantic changes were made by the handoff milestone.
- API Surface: `POST /api/content-projects`, `POST /api/content-projects/{contentProjectId}/generate-idea-jobs`, and `GET /api/jobs/{jobId}`.
- API Behavior: create persists a Draft ContentProject; enqueue persists a queued `GenerateIdea` Job and returns its ID; query maps Queued/Running/Succeeded/Failed/Cancelled to `queued`/`running`/`completed`/`failed`/`cancelled`, exposes the validated typed GenerateIdea result when complete, and omits worker/lease details.
- Validation: Release build PASS with 0 warnings/errors. Focused GenerateIdea/API tests PASS, 26/26 in the current working tree. HTTP smoke PASS for root 200 and ProblemDetails 400 on blank project/input, empty GUID, and malformed project/job GUID.
- Runtime Smoke: BLOCKED before API startup; PostgreSQL was not listening on `127.0.0.1:5432`, and the Windows Docker CLI reported that Docker Desktop WSL2 integration is not enabled. No database, API, worker, or application configuration was changed.
- Phase 0 Local Developer AI: Ollama `0.34.1` is active as the WSL2 system service at `http://127.0.0.1:11434`; `OLLAMA_CONTEXT_LENGTH=49152` is verified. Qwen Code is currently `0.24.0` (the prior checkpoint recorded `0.23.4`) and uses `Qwen Code -> Anthropic-compatible provider -> Ollama`.
- Coding Models: primary `qwen3.6:27b-coding`; secondary `qwen3.6:27b-q4_K_M`; experimental `qwen3.8:27b-q4_K_M` remains unsuitable for multi-step Qwen Code tool workflows due HTTP 500 `no user query found in messages`; `qwen3.5:9b` is removed from active coding-agent configuration.
- Qwen Routing: developer-productivity only. Use Qwen for bounded, repetitive, localized, low-risk work; use Codex for architecture, security/authorization, concurrency, migrations, durable jobs, destructive operations, ambiguous cross-cutting work, and complex debugging. Human diff review is mandatory before committing Qwen changes; do not build a compatibility platform around Qwen/Ollama.
- Pending Human Review: the pre-existing Qwen patch in `GenerateIdeaResult.cs` and `GenerateIdeaContractsTests.cs`, plus `.qwen/settings.json` and the untracked PRD, remain intentionally uncommitted.
- Next Recommended Step: enable Docker Desktop WSL integration, start the repository PostgreSQL service, and complete one GenerateIdea HTTP/database/worker smoke. After it passes, the next product milestone is the smallest bounded GenerateScript vertical slice.
