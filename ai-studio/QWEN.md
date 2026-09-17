# Qwen Code guide

## Scope

- Work on one bounded task at a time. Consult the relevant `docs/agent/REPO_MAP.md` section for navigation, CHECKPOINT when current state matters, and relevant LESSONS when prior validated knowledge matters.
- Prefer exact files and targeted symbol searches. Do not scan the repository or read the full PRD automatically.
- Prefer repository-controlled knowledge over automatic/private memory. Do not write managed memory or create skills.
- Touch the fewest files necessary, follow established patterns, and avoid unrelated edits or speculative abstractions.

## Guardrails

- Preserve the existing architecture. PostgreSQL remains the durable job authority, and `IAiTextGenerator` remains the current AI boundary.
- Do not change architecture, security design, database migrations, concurrency, or durable-job semantics.
- Do not perform destructive operations or modify `global.json` to bypass a missing SDK, tool, or runtime. Stop and report the exact blocker instead.
- Do not commit or push. Leave every patch uncommitted for human/Codex review.
- Never edit the same working tree concurrently with Codex. Preserve all existing user changes.

## Verification and handoff

- Run only targeted build/tests for the changed behavior.
- Report changed files, behavioral effect, exact validation results, and unresolved issues.
