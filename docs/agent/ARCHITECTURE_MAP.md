# Architecture Map

## Composition and UI

- `src/AIStudio.Api/Program.cs`: backend composition root, middleware, health routes, and loopback host.
- `src/AIStudio.Api/ErrorHandling/`: global Problem Details exception handling.
- `src/AIStudio.Api/Health/`: public health response shape.
- `src/AIStudio.Web/`: React/TypeScript/Vite browser foundation; product UI awaits Figma handoff.

## Local runtime and toolchain

- `compose.yaml`: single PostgreSQL 18.6 development service, health check, loopback port, and named volume.
- `.env.example`: Compose variables and API connection-string override; copy to ignored `.env`.
- `global.json`: .NET 10.0.401 SDK and Microsoft.Testing.Platform selection.
- `.nvmrc` and `src/AIStudio.Web/package.json`: Node 24.17.0 LTS/npm compatibility.
- `README.md`: WSL-first setup, database, migration, test, and run commands.

## Core boundaries

- `src/AIStudio.Domain/Content/`: ContentProject root and explicit content lifecycle.
- `src/AIStudio.Domain/Jobs/`: persistent Job lifecycle, retry rules, JSON contract validation, and claim-preparation fields.
- `src/AIStudio.Application/Abstractions/`: core-owned contracts; no generic repository or transport framework.
- `src/AIStudio.Infrastructure/Persistence/ApplicationDbContext.cs`: EF Core context and domain sets.
- `src/AIStudio.Infrastructure/Persistence/Configurations/`: explicit PostgreSQL mappings, constraints, relationships, and indexes.
- `src/AIStudio.Infrastructure/Persistence/Migrations/`: first real schema migration and model snapshot.
- `src/AIStudio.Infrastructure/Health/DatabaseHealthCheck.cs`: PostgreSQL readiness.
- `tests/AIStudio.Tests/`: domain unit tests and real PostgreSQL persistence validation.

## Navigation

- Product intent: `docs/product/INDEX.md`
- Architecture and extraction direction: `docs/development/ARCHITECTURE.md`
- Current detailed state: `docs/context/STATE.md`
- Latest concise handoff: `docs/agent/CHECKPOINT.md`
