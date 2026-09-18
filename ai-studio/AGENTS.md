# Agent guide — Local AI Content Studio

This guide applies to `ai-studio/`. Run product commands from this directory. The sibling `local-ai-infra/` solution manages reusable local runtime concerns and is not a Content Studio runtime dependency.

## Start small

- Read `docs/agent/CHECKPOINT.md`, then the relevant parts of `docs/context/PROJECT.md` and `docs/context/STATE.md` at session start unless already in context. Consult relevant sections of `docs/agent/LESSONS.md` for validated knowledge and `docs/agent/REPO_MAP.md` for navigation. Also follow the repository-wide `../AGENTS.md`. Inspect Git status before editing; cached state can be stale.
- Use the task routing table in PROJECT, `rg`, and bounded reads. Do not read all docs, generated files, LESSONS, or REPO_MAP by default.
- For intended product behavior, read `docs/product/PRD_INDEX.md` first, then only the smallest relevant `docs/product/parts/` or `docs/product/execution-context/` file. Do not read the monolithic source `docs/product/_source/Local_AI_Ecosystem_PRD_v0.9.2.md` unless a split file is ambiguous or incomplete; the frozen source remains authoritative.
- Source hierarchy: the latest user request authorizes scope; code defines current behavior; the latest PRD defines intended behavior; CHECKPOINT/PROJECT/STATE summarize current execution. Distinguish verified implementation, requirements, proposals, and environment state.
- A missing SDK, container, model, or tool is an environment blocker, not a repository defect. Never change `global.json` or architecture merely to bypass it.
- Communicate in Indonesian; use English code identifiers. Keep updates and handoffs concise.

## Development rules

- Baseline: .NET 10 modular monolith, React/TypeScript/Vite, PostgreSQL, local filesystem, Ollama, FFmpeg, and a Python transcription runner. See `docs/development/ARCHITECTURE.md` before changing boundaries.
- Treat PostgreSQL as the durable job authority. Persist job inputs, separate content/job state, and preserve claiming, leases, retry, ownership, and recovery; `IAiTextGenerator` is the product-AI boundary.
- The user is designing Figma in parallel. Backend/domain work can proceed independently. Follow `docs/design/FIGMA-HANDOFF.md` for UI work; do not invent final branding or treat absent Figma as a backend blocker.
- Keep product AI local, with no cloud/paid fallback. The developer coding model (`qwen3.6:27b-coding`) and validated Content Studio runtime model (`qwen3.8:27b-q4_K_M`) have separate roles. Record actual checks; never invent benchmarks or approvals.
- Bind services to loopback by default. Validate paths, media, and scene schemas. Pass structured process arguments; never execute model-generated shell commands.
- Do not add deferred infrastructure or autonomous publishing without a current need and authorization. Repo development does not itself authorize public uploads, purchases, or external messages.
- Avoid speculative abstractions and unrelated edits. Do not change architecture, security, concurrency, migrations, durable-job semantics, or `global.json` unless the current task explicitly requires it.
- Preserve existing worktree changes and never let agents edit it concurrently. Do not spawn subagents unless the user explicitly requests delegation.

## Editing fallback

- If the built-in editor or patch helper fails because bubblewrap is unavailable, do not retry it. Use an available system patch/edit utility immediately, preserve the requested scope, inspect the resulting diff, and run the requested validation. Treat bubblewrap absence as a tooling limitation, not a repository failure.

## Verification and handoff

- Run targeted, risk-based checks. Escalate architecture, security, schema/migration, concurrency, durable-job, destructive, and ambiguous cross-cutting work.
- Record exact commands and results in STATE; distinguish passed, failed, unavailable, and not run.
- Update STATE after meaningful work: completed work, current task, next step, checks, blockers. Replace stale detail; do not append full conversation logs.
- Update PROJECT when stable facts change and `docs/development/DECISIONS.md` for architectural decisions. Keep proposed choices explicitly labeled.
- Handoffs state changed files, behavior, validation, blockers, and uncommitted work. Add a LESSON only after review when it is stable and evidence-backed; never store speculation as established knowledge.
- When the source PRD changes, refresh the affected `docs/product/parts/` or `execution-context/` file, then `docs/product/PRD_INDEX.md`, `docs/product/SECTION_MAP.md`, and the source checksum. Code establishes current behavior; PRD establishes intended behavior. Report discrepancies.
- Keep AGENTS and each startup context file compact (target at most 600 words each). Store detailed evidence in linked task documents only when useful.
- Keep secrets, local media, model weights, logs, and build artifacts out of Git. Context documents contain summaries and safe references only.
