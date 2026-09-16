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
| D-007 | 2026-09-15 | User direction | Use a modular monolith for the core with explicit module/contracts boundaries and evolve, only when demonstrated, toward **Modular Core + Specialized Workers**. Resource-heavy AI, research, speech, media, rendering, publishing, and analytics workloads are extraction candidates. P1 adds no broker, service discovery, gateway, Kubernetes, per-module databases, distributed tracing, or microservice networking. |
| D-008 | 2026-09-15 | Implemented | EF Core/Npgsql and `dotnet-ef` are configured, but no empty migration is created. The first migration accompanies a real Core Domain persistence model. |
| D-009 | 2026-09-15 | User direction | Default development is Windows + WSL2: application/tooling execute in WSL; browser runs on Windows; future PostgreSQL Compose runs through Docker Desktop WSL integration with one named volume and environment credentials. |
| D-010 | 2026-09-15 | Superseded by D-011 | Local PostgreSQL initially used PostgreSQL 16 Alpine; the runtime was upgraded during the first persistence milestone. |
| D-011 | 2026-09-15 | Implemented | Local PostgreSQL uses PostgreSQL 18.6 Alpine. The prior volume was verified empty of business tables before clean recreation for the PostgreSQL 18 data layout; loopback exposure and one named volume remain. |
| D-012 | 2026-09-15 | Implemented | WSL uses persistent per-user .NET SDK 10.0.401 and Node.js 24.17.0 LTS/npm 11.13.0, pinned by repository version files. Temporary SDK installs are not part of validation. |
| D-013 | 2026-09-15 | Implemented | ContentProject and Job use application-generated UUIDs, UTC DateTimeOffset, string-backed enums, and explicit EF mappings; Job payload/result use JSONB. Only WorkerId and LeaseExpiresAt prepare for PostgreSQL claiming. ContentItem is deferred. |
| D-014 | 2026-09-16 | Implemented | Persistent jobs are claimed one at a time in a PostgreSQL Read Committed transaction using deterministic ordering, FOR UPDATE SKIP LOCKED, and UPDATE RETURNING. Completion, failure, and lease renewal are ownership-guarded; no broker or distributed lock is introduced. |
| D-015 | 2026-09-16 | Implemented | Application owns a small provider-neutral text-generation contract; Infrastructure integrates Ollama directly through its stable HTTP chat API. Development defaults to `gemma3:4b`, plain text or JSON-object output, and no provider retry framework. Real workload handlers remain deferred. |

Future decisions: API contracts, target hardware, model/license activation, and any baseline changes. Record evidence when those are made; do not invent approvals or benchmark results.
