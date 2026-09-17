# Shared Agent Lessons

Only stable, reviewed, evidence-backed knowledge belongs here.

## Environment

- Never modify `global.json` to work around a missing SDK; report the environment blocker.
- Bubblewrap absence is a tooling limitation. Switch once to the available system patch/edit utility, preserve scope, review the diff, and validate.

## Local AI

- `qwen3.6:27b-coding` is the developer coding model; `qwen3.8:27b-q4_K_M` is the runtime model validated for Content Studio GenerateIdea. Coding-agent and product-runtime model choices are separate.
- `qwen3.6:27b-q4_K_M` was unreliable for GenerateIdea strict structured output. `qwen3.8:27b-q4_K_M` completed the durable GenerateIdea flow with structured result persistence and HTTP retrieval.
- Direct `qwen3.6:27b-coding` inference is usable locally. Long delays were observed around Qwen Code compaction/tool orchestration, but the exact cause was not proven.

## Workflow

- Qwen-generated changes require human/Codex review before commit.
- Prefer targeted tests and preserve PostgreSQL durable-job claiming, lease, retry, ownership, and recovery semantics.
