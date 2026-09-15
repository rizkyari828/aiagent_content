# Decision register

Use short entries with date, status, basis, and consequence. Source requirements are distinguished from implementation choices.

| ID | Date | Status | Decision and basis |
| --- | --- | --- | --- |
| D-001 | 2026-09-15 | User instruction | Begin with agent documents and repo context before application code; user works on Figma in parallel. |
| D-002 | 2026-09-15 | PRD baseline | .NET 10 modular monolith, React/TypeScript/Vite, PostgreSQL, local media; no broker/cache/vector infrastructure in P1. PRD §§1, 13, 24. |
| D-003 | 2026-09-15 | PRD baseline | Local-only product AI and Rp0 additional spend; recorded pilot narration, manual upload/metrics. PRD §§3–4, 17, 20–21. |
| D-004 | 2026-09-15 | Implemented documentation | Root AGENTS routes to compact PROJECT/STATE and task-specific docs. Keep one unchanged PRD copy, checksum, section index. No raw conversation cache or generated summaries loaded wholesale. |
| D-005 | 2026-09-15 | Working implementation plan | DEV slices group existing P1 work; no acceptance criteria removed. Previous review suggestions do not replace the audience, timing, or other PRD requirements. |
| D-006 | 2026-09-15 | Pending design | User Figma will supply visual direction. No final layout/tokens/components approved yet; backend can proceed. |

Future decisions: exact tooling pins, API contracts, target hardware, model/license activation, and any baseline changes. Record evidence when those are made; do not invent approvals or benchmark results.
