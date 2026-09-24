# Agent guide — Content Studio repository

## Start with the owning solution

- Inspect `git status` before editing, then scope the task to exactly one solution unless the request explicitly crosses boundaries.
- For Content Studio work, enter `ai-studio/` and follow `ai-studio/AGENTS.md`. Its checkpoint, project state, lessons, and repository map are authoritative for the product.
- For reusable local runtime tooling, work in the separate sibling repository `local-ai-infra/` (resolve it with `LAI_REPO`, default `../local-ai-infra`) and follow its own `AGENTS.md`.
- Use targeted reads and searches. Do not scan both solutions or read full specifications by default.
- Communicate in Indonesian; use English code identifiers. Keep updates and handoffs concise.

## Repository boundaries

- `ai-studio/` owns the Content Studio product, domain knowledge, application configuration, durable jobs, API, web client, tests, and product documentation.
- The separate sibling repository `local-ai-infra/` owns reusable local AI configuration, profiles, verification, benchmarks, telemetry formats, evals, and learning-data conventions. It is not part of this repository.
- Local AI infrastructure describes **how** local AI runs. Content Studio describes **what** the product does and which model it requests.
- Content Studio must not acquire a runtime dependency on `local-ai-infra/` without an explicit architectural decision.
- Keep repository-wide files at the root. Keep solution-specific guidance and configuration inside the owning solution.

## Shared safety and verification

- Preserve user changes and untracked files. Never expose secrets, local media, model weights, logs, or credentials.
- Do not create nested Git repositories or rewrite history. Avoid unrelated edits and speculative infrastructure.
- Treat missing SDKs, containers, models, or tools as environment limitations; do not alter architecture to bypass them.
- Run targeted validation from the owning solution directory and record exact results in that solution's state/checkpoint documentation when required by its guide.
- Commit only the intended scope. No force push.

## Repository Context Protocol

Before broad implementation inspection, every coding agent (Codex, DeepSeek, Qwen, or any other) must:

1. Read `ai-studio/docs/agent/CHECKPOINT.md`, `ai-studio/docs/context/STATE.md`, and `ai-studio/docs/agent/REPO_MAP.md`.
2. Read `ai-studio/docs/development/DECISIONS.md` only when the task touches architecture, existing design decisions, trade-offs, or behavior whose rationale matters.
3. Treat those files as the initial repository context.
4. Do not recursively scan or read the entire repository by default.
5. Use `REPO_MAP.md` to locate the relevant modules first.
6. Prefer targeted search, symbol lookup, `find`, and direct file inspection.
7. Expand repository inspection only when the docs are insufficient, conflict with implementation, reference stale paths, or code evidence is required.
8. Source code remains authoritative. Documentation accelerates discovery but never overrides verified code.
9. After meaningful implementation changes: update `CHECKPOINT.md` when the resume point changes, `STATE.md` when verified system state changes, `REPO_MAP.md` when important structure changes, and `DECISIONS.md` when a durable technical decision changes.
10. Documentation must describe verified state only.
11. Before ending a substantial coding task, verify whether these docs need updates.
12. Do not rewrite these docs unnecessarily when repository state did not materially change.

```
CHECKPOINT = where to resume
STATE      = what is true
REPO_MAP   = where things live
DECISIONS  = why important choices exist
```

This rule is not model-specific. For the separate `local-ai-infra/` repository, follow its own `AGENTS.md`.
