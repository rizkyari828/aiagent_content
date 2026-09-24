# Qwen Code guide — repository-wide

Qwen Code loads this file automatically from the project root. It supplements
`AGENTS.md`; the owning solution's `AGENTS.md`/`QWEN.md` still wins for its scope.

## Start every task

- Read the root `AGENTS.md`, then enter the owning solution and follow its guide
  (`ai-studio/AGENTS.md` + `ai-studio/QWEN.md`, or the separate `local-ai-infra`
  repository's `AGENTS.md`).
- Work on exactly one bounded task. Name the target files before reading them.
- Communicate in Indonesian; use English code identifiers.

## Bounded execution (avoid context loss and stalls)

- Prefer exact paths, `rg`/`grep_search`, and bounded reads over repo-wide globs.
- For large docs (PRD, REPO_MAP, LESSONS, CHECKPOINT): search the heading first,
  then read a bounded line range. Never read the full PRD for a bounded task.
- Do not spawn Explore or other subagents for bounded local coding unless the user
  explicitly asks for delegation.
- Do not re-read a file without a concrete reason (it changed, or a named gap).
- Start editing once the minimum evidence is available. Stop and report if context
  keeps growing without a clear next edit.
- Validate only the changed behavior (targeted build/tests), then summarize files,
  effect, and exact results.

## Runtime awareness (measured on this workstation)

- `qwen3.6:27b-coding` is ~18 GB and is partially CPU-offloaded on a 16 GB GPU.
  Measured generation is ~14 tok/s at short context but ~5 tok/s at ~20-27K tokens.
- Anthropic-compatible Ollama answers always include a `thinking` block by default,
  and `budget_tokens` is ignored, so reasoning effort is not controllable here.
  Keep tasks small; do not rely on `/effort` to speed things up.
- Fewer, smaller, targeted turns beat many exploratory turns. Aim to keep the live
  context well under the profile limit before editing.
- Do not hand-edit managed Qwen settings or the Ollama override. Use
  `../local-ai-infra/scripts/ai-profile`; see `../local-ai-infra/docs/QWEN_WORKFLOW.md`.
