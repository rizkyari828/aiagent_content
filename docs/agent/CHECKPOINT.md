# Agent Checkpoint

Updated: 2026-09-16

- Current Phase: P1.
- Current Milestone: GenerateIdea runtime smoke reached a real terminal state through PostgreSQL, worker, and local AI, but structured-result completion remains blocked. The Qwen result-validation patch remains reviewed with changes requested.
- API Surface: `POST /api/content-projects`, `POST /api/content-projects/{contentProjectId}/generate-idea-jobs`, and `GET /api/jobs/{jobId}`.
- API Behavior: create persists a Draft ContentProject; enqueue persists a queued `GenerateIdea` Job and returns its ID; query maps Queued/Running/Succeeded/Failed/Cancelled to `queued`/`running`/`completed`/`failed`/`cancelled`, exposes the validated typed GenerateIdea result when complete, and omits worker/lease details.
- Validation: Docker/Compose and PostgreSQL health PASS; existing migration is current. One HTTP flow persisted a Draft ContentProject and durable job, observed `queued -> running -> failed`, and confirmed claim/retry/terminal persistence plus safe HTTP projection.
- Runtime Smoke: BLOCKED at structured local-AI completion. A temporary environment override selected Ollama `qwen3.6:27b-coding` without changing the repository default; each built-in attempt returned HTTP 200 but failed JSON validation. The job exhausted retries at `2/2` with `ai_malformed_response` and no result. AI Infrastructure Foundation v1 is not DONE or FROZEN.
- Phase 0 Local Developer AI: Ollama `0.34.1` is active as the WSL2 system service at `http://127.0.0.1:11434`; `OLLAMA_CONTEXT_LENGTH=49152` is verified. Qwen Code is currently `0.24.0` (the prior checkpoint recorded `0.23.4`) and uses `Qwen Code -> Anthropic-compatible provider -> Ollama`.
- Coding Models: primary `qwen3.6:27b-coding`; secondary `qwen3.6:27b-q4_K_M`; experimental `qwen3.8:27b-q4_K_M` remains unsuitable for multi-step Qwen Code tool workflows due HTTP 500 `no user query found in messages`; `qwen3.5:9b` is removed from active coding-agent configuration.
- Qwen Routing: developer-productivity only. Use Qwen for bounded, repetitive, localized, low-risk work; use Codex for architecture, security/authorization, concurrency, migrations, durable jobs, destructive operations, ambiguous cross-cutting work, and complex debugging. Human diff review is mandatory before committing Qwen changes; do not build a compatibility platform around Qwen/Ollama.
- Qwen Patch Review: REVIEWED — changes requested. `Serialize()` validates trimmed values but serializes the untrimmed record, so it does not yet match deserialization normalization; add an assertion covering trimmed serialized output. The patch remains uncommitted for human approval.
- Next Recommended Step: diagnose the installed model's structured-JSON compatibility through existing configuration, then repeat one GenerateIdea smoke after resolution. GenerateScript remains a separate later milestone.
