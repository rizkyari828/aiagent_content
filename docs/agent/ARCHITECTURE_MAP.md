# Architecture Map

## Composition and UI

- src/AIStudio.Api/Program.cs: backend composition root, middleware, hosted worker registration through Infrastructure, and health routes.
- src/AIStudio.Api/ErrorHandling/: global Problem Details exception handling.
- src/AIStudio.Api/Health/: public health response shape.
- src/AIStudio.Web/: React/TypeScript/Vite browser foundation; product UI awaits Figma handoff.

## Local runtime and toolchain

- compose.yaml: single PostgreSQL 18.6 development service, health check, loopback port, and named volume.
- .env.example: Compose variables and API connection-string override; copy to ignored .env.
- global.json: .NET 10.0.401 SDK and Microsoft.Testing.Platform selection.
- .nvmrc and src/AIStudio.Web/package.json: Node 24.17.0 LTS/npm compatibility.
- README.md: WSL-first setup, database, migration, test, and run commands.

## Core and persistence

- src/AIStudio.Domain/Content/: ContentProject root and explicit content lifecycle.
- src/AIStudio.Domain/Jobs/: persistent Job lifecycle, retry rules, JSON validation, and lease fields.
- src/AIStudio.Application/AI/: provider-neutral text-generation contract, serializable request/response DTOs, response format, and application-level errors.
- src/AIStudio.Application/Jobs/: serializable claimed-job DTO plus focused queue and handler contracts.
- src/AIStudio.Infrastructure/AI/: typed HTTP Ollama adapter, provider DTO mapping, configuration, validation, and error translation.
- src/AIStudio.Infrastructure/Persistence/: EF Core context, mappings, and first real migration.
- src/AIStudio.Infrastructure/Jobs/PostgreSqlJobQueue.cs: parameterized PostgreSQL atomic claim, ownership-guarded completion/failure, retry, and lease operations.
- src/AIStudio.Infrastructure/Jobs/JobProcessor.cs: handler resolution, execution, lease renewal, and lifecycle logging.
- src/AIStudio.Infrastructure/Jobs/JobWorker.cs: hosted polling loop and graceful cancellation.
- src/AIStudio.Infrastructure/Jobs/PlaceholderJobHandler.cs: orchestration-only placeholder; no business workload.
- tests/AIStudio.Tests/: domain, worker, real PostgreSQL concurrency/integration, and isolated Ollama adapter checks.

## Navigation

- Product intent: docs/product/INDEX.md
- Architecture and extraction direction: docs/development/ARCHITECTURE.md
- Current detailed state: docs/context/STATE.md
- Latest concise handoff: docs/agent/CHECKPOINT.md
