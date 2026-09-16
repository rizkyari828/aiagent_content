# Agent Checkpoint

Updated: 2026-09-16

- Current Phase: P1.
- Current Milestone: Minimal GenerateIdea HTTP API vertical slice implemented; no schema or durable-job semantic changes.
- API Surface: `POST /api/content-projects`, `POST /api/content-projects/{contentProjectId}/generate-idea-jobs`, and `GET /api/jobs/{jobId}`.
- API Behavior: create persists a Draft ContentProject; enqueue persists a queued `GenerateIdea` Job and returns its ID; query maps Queued/Running/Succeeded/Failed/Cancelled to `queued`/`running`/`completed`/`failed`/`cancelled`, exposes the validated typed GenerateIdea result when complete, and omits worker/lease details.
- Validation: Release build PASS with 0 warnings/errors. Focused GenerateIdea/API tests PASS, 26/26 in the current working tree. HTTP smoke PASS for root 200 and ProblemDetails 400 on blank project/input, empty GUID, and malformed project/job GUID.
- Environment Blocker: database-backed HTTP create/enqueue/query smoke was not completed because PostgreSQL was not listening on `127.0.0.1:5432` and the Docker CLI/WSL integration was unavailable. The failed create persisted no row.
- Phase 0 Local Developer AI: operationally accepted from the manual environment verification. Ollama `0.34.1` runs as the WSL2 system service at `http://127.0.0.1:11434` with increased coding context. Qwen Code `0.23.4` uses the successful Anthropic-compatible provider path to Ollama and is connected to VS Code.
- Coding Models: primary `qwen3.6:27b-coding`; secondary `qwen3.6:27b-q4_K_M`; experimental `qwen3.8:27b-q4_K_M` remains unsuitable for multi-step Qwen Code tool workflows due HTTP 500 `no user query found in messages`; `qwen3.5:9b` is removed from active coding-agent configuration.
- Qwen Routing: allowed only for scoped, repetitive, low-risk work with mandatory human diff review before commit. Escalate architecture, security, authorization, concurrency, migrations, durable-job semantics, destructive operations, and ambiguous work to Codex. Do not build a Qwen/Ollama compatibility framework.
- Pending Human Review: the pre-existing Qwen patch in `GenerateIdeaResult.cs` and `GenerateIdeaContractsTests.cs`, plus `.qwen/settings.json` and the untracked PRD, are intentionally excluded from the API milestone commit.
- Next Recommended Step: restore Docker Desktop WSL integration/PostgreSQL, run the three-endpoint persistence smoke with the worker disabled, then run one completed-job HTTP check through the existing worker before starting GenerateScript or frontend work.
