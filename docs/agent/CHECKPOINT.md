# Agent Checkpoint

Updated: 2026-09-16

- Current Phase: P1.
- Current Milestone: GenerateIdea HTTP API and agent handoff guidance are implemented. Runtime validation remains blocked; the Qwen result-validation patch was reviewed with changes requested.
- API Surface: `POST /api/content-projects`, `POST /api/content-projects/{contentProjectId}/generate-idea-jobs`, and `GET /api/jobs/{jobId}`.
- API Behavior: create persists a Draft ContentProject; enqueue persists a queued `GenerateIdea` Job and returns its ID; query maps Queued/Running/Succeeded/Failed/Cancelled to `queued`/`running`/`completed`/`failed`/`cancelled`, exposes the validated typed GenerateIdea result when complete, and omits worker/lease details.
- Validation: prior Release build and focused GenerateIdea/API tests PASS, 26/26. Qwen patch review test filter `GenerateIdeaContractsTests` PASS, 7/7. Database-backed end-to-end smoke was not run.
- Runtime Smoke: BLOCKED again before API startup; Docker Desktop WSL2 integration remains unavailable and PostgreSQL is not listening on `127.0.0.1:5432`. Ollama `0.34.1` remains reachable locally. AI Infrastructure Foundation v1 is not yet runtime-validated or frozen.
- Phase 0 Local Developer AI: Ollama `0.34.1` is active as the WSL2 system service at `http://127.0.0.1:11434`; `OLLAMA_CONTEXT_LENGTH=49152` is verified. Qwen Code is currently `0.24.0` (the prior checkpoint recorded `0.23.4`) and uses `Qwen Code -> Anthropic-compatible provider -> Ollama`.
- Coding Models: primary `qwen3.6:27b-coding`; secondary `qwen3.6:27b-q4_K_M`; experimental `qwen3.8:27b-q4_K_M` remains unsuitable for multi-step Qwen Code tool workflows due HTTP 500 `no user query found in messages`; `qwen3.5:9b` is removed from active coding-agent configuration.
- Qwen Routing: developer-productivity only. Use Qwen for bounded, repetitive, localized, low-risk work; use Codex for architecture, security/authorization, concurrency, migrations, durable jobs, destructive operations, ambiguous cross-cutting work, and complex debugging. Human diff review is mandatory before committing Qwen changes; do not build a compatibility platform around Qwen/Ollama.
- Qwen Patch Review: REVIEWED — changes requested. `Serialize()` validates trimmed values but serializes the untrimmed record, so it does not yet match deserialization normalization; add an assertion covering trimmed serialized output. The patch remains uncommitted for human approval.
- Next Recommended Step: revise and re-review the bounded Qwen patch, enable Docker Desktop WSL integration, then complete one GenerateIdea HTTP/PostgreSQL/worker/local-AI smoke. GenerateScript remains a separate later milestone.
