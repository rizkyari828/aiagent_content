# Qwen Code guide

## Scope

- Work on one bounded task at a time. Read `docs/agent/CHECKPOINT.md` when current project state matters.
- Prefer targeted symbol and file searches. Do not scan the repository or read the full PRD unless the task explicitly requires it.
- Touch the fewest files necessary, follow established patterns, and avoid unrelated edits or speculative abstractions.

## Guardrails

- Preserve the existing architecture. PostgreSQL remains the durable job authority, and `IAiTextGenerator` remains the current AI boundary.
- Do not change architecture, security design, database migrations, concurrency, or durable-job semantics.
- Do not perform destructive operations or modify `global.json` to bypass a missing SDK, tool, or runtime. Stop and report the exact blocker instead.
- Do not commit, push, write managed memory, or create skills. Leave every patch uncommitted for human review.
- Never edit the same working tree concurrently with Codex. Preserve all existing user changes.

## Verification and handoff

- Run only targeted build/tests for the changed behavior.
- Report changed files, behavioral effect, exact validation results, and unresolved issues.
