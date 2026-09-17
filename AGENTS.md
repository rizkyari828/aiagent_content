# Agent guide — AI monorepo

## Start with the owning solution

- Inspect `git status` before editing, then scope the task to exactly one solution unless the request explicitly crosses boundaries.
- For Content Studio work, enter `ai-studio/` and follow `ai-studio/AGENTS.md`. Its checkpoint, project state, lessons, and repository map are authoritative for the product.
- For reusable local runtime tooling, enter `local-ai-infra/` and follow `local-ai-infra/AGENTS.md`.
- Use targeted reads and searches. Do not scan both solutions or read full specifications by default.
- Communicate in Indonesian; use English code identifiers. Keep updates and handoffs concise.

## Repository boundaries

- `ai-studio/` owns the Content Studio product, domain knowledge, application configuration, durable jobs, API, web client, tests, and product documentation.
- `local-ai-infra/` owns reusable local AI configuration, profiles, verification, benchmarks, telemetry formats, evals, and learning-data conventions.
- Local AI infrastructure describes **how** local AI runs. Content Studio describes **what** the product does and which model it requests.
- Content Studio must not acquire a runtime dependency on `local-ai-infra/` without an explicit architectural decision.
- Keep repository-wide files at the root. Keep solution-specific guidance and configuration inside the owning solution.

## Shared safety and verification

- Preserve user changes and untracked files. Never expose secrets, local media, model weights, logs, or credentials.
- Do not create nested Git repositories or rewrite history. Avoid unrelated edits and speculative infrastructure.
- Treat missing SDKs, containers, models, or tools as environment limitations; do not alter architecture to bypass them.
- Run targeted validation from the owning solution directory and record exact results in that solution's state/checkpoint documentation when required by its guide.
- Commit only the intended scope. No force push.
