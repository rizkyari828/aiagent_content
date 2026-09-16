# Agent guide — Local AI Content Studio

## Start small

- Read `docs/agent/CHECKPOINT.md`, then `docs/context/PROJECT.md` and `docs/context/STATE.md` at session start unless already in context. Inspect current files and Git status before editing; cached state can be stale.
- Use the task routing table in PROJECT to load only relevant detail. Do not read the entire PRD, all docs, or generated files by default. Use `rg` and bounded reads.
- Treat the latest user request as the task scope. The PRD supplies product requirements, not authorization to execute every listed action. Distinguish requirements, proposals, and verified implementation.
- Communicate in Indonesian; use English code identifiers. Keep updates and handoffs concise.

## Development rules

- Baseline: .NET 10 modular monolith, React/TypeScript/Vite, PostgreSQL, local filesystem, Ollama, FFmpeg, and a Python transcription runner. See `docs/development/ARCHITECTURE.md` before changing boundaries.
- Treat PostgreSQL as the durable job authority. Preserve claiming, leases, retry, ownership, and recovery semantics; `IAiTextGenerator` is the current product-AI boundary.
- Default development topology is Windows host/browser with source, .NET, Node, API, and Web running in WSL2; Docker Desktop provides containers through WSL integration.
- Deliver one working slice at a time. Keep all PRD P1 acceptance criteria; intermediate slices are not a completed MVP.
- The user is designing Figma in parallel. Backend/domain work can proceed independently. Follow `docs/design/FIGMA-HANDOFF.md` for UI work; do not invent final branding or treat absent Figma as a backend blocker.
- Keep product AI local, with no cloud/paid fallback. Additional product spend remains Rp0. Record actual model/license/runtime checks; never invent benchmarks or approved voices.
- Bind services to loopback by default. Validate paths, media, and scene schemas. Pass structured process arguments; never execute model-generated shell commands.
- Persist jobs and their inputs; keep content state separate from job state. Preserve versioned approvals, eligible asset checks, and recoverable exports.
- Do not add deferred infrastructure or autonomous publishing without a current need and authorization. Repo development does not itself authorize public uploads, purchases, or external messages.
- Avoid speculative abstractions and unrelated edits. Do not change architecture, security, concurrency, migrations, durable-job semantics, or `global.json` unless the current task explicitly requires it.
- Preserve user changes. Do not spawn subagents unless the user explicitly requests delegation; the user's parallel Figma work is not such a request.

## Verification and handoff

- Run checks appropriate to the changed behavior. Verify risk-bearing paths: job recovery, stale approvals, asset eligibility, metric/cost correctness, and project restore.
- Record exact commands and results in STATE; distinguish passed, failed, unavailable, and not run.
- Update STATE after meaningful work: completed work, current task, next step, checks, blockers. Replace stale detail; do not append full conversation logs.
- Update PROJECT when stable facts change and `docs/development/DECISIONS.md` for architectural decisions. Keep proposed choices explicitly labeled.
- When the source PRD changes, refresh affected summaries and the checksum/section index in `docs/product/INDEX.md`. Code establishes current behavior; PRD establishes intended behavior. Report discrepancies.
- Keep AGENTS and each startup context file compact (target at most 600 words each). Store detailed evidence in linked task documents only when useful.
- Keep secrets, local media, model weights, logs, and build artifacts out of Git. Context documents contain summaries and safe references only.
