# Work state

Updated: 2026-09-15.

## Current milestone

P1 Core Domain + Persistent Job Model is complete. The application remains one modular monolith; the overall P1/MVP is not complete.

## Implemented

- Persistent WSL toolchain: .NET SDK 10.0.401 in `~/.dotnet`; Node.js 24.17.0 LTS and npm 11.13.0 through nvm. `global.json`, `.nvmrc`, package engines, and Microsoft.Testing.Platform pin the repository workflow.
- PostgreSQL development runtime upgraded from 16.10 to 18.6 Alpine. The verified-empty local volume was recreated and mounted at the PostgreSQL 18 layout `/var/lib/postgresql`; loopback-only exposure and the named volume remain.
- `ContentProject`: application-generated UUID, title/brief, full content lifecycle, explicit valid/rework transitions, and UTC `DateTimeOffset` timestamps.
- `Job`: business-oriented type, queued/running/succeeded/failed/cancelled lifecycle, bounded retry behavior, input hash, JSONB payload/result, error details, UTC timestamps, and minimal worker/lease fields for the next claiming slice.
- Explicit EF Core mappings provide snake_case tables/columns, string enums, UUID keys, JSONB, FK restriction, retry constraints, and minimal status/created-time polling indexes.
- First real migration `20260915160805_InitialCoreDomain` creates only `content_projects`, `jobs`, and EF migration history.
- One test project covers content transitions, Job lifecycle/retries/JSON validation, and real PostgreSQL persistence/query/JSONB round-trips. `ContentItem` remains deferred because this slice has no separate persisted artifact requiring it.

## Verification

- PASS - persistent toolchain: .NET 10.0.401, Node 24.17.0 LTS, npm 11.13.0, Docker 29.1.3, Compose v2.40.3, PostgreSQL 18.6.
- PASS - `dotnet restore AIStudio.slnx` and Release build; 0 warnings/errors.
- PASS - `dotnet format AIStudio.slnx --verify-no-changes --no-restore`; 0 files changed.
- PASS - `dotnet test AIStudio.slnx --no-build --configuration Release`; 11 passed, 0 failed.
- PASS - NuGet vulnerability, deprecation, and outdated checks; none reported.
- PASS - `npm ci`, production build, `npm audit`, and direct outdated check; 0 vulnerabilities/updates.
- PASS - Compose validation, PostgreSQL health, migration apply/list, table/column verification, and zero pending migrations.
- PASS - persistence test inserted, queried, round-tripped, and removed disposable rows; final project/job row counts are zero.
- PASS - API startup/DI/configuration and PostgreSQL connectivity; root, liveness, and readiness returned HTTP 200.

## Known issues and next task

Docker Desktop's saved registry credentials still reject normal pulls; the official PostgreSQL 18.6 image was pulled anonymously with an isolated temporary Docker config. No product API/worker execution was added. Next recommended milestone: **P1 Persistent Worker Execution + Safe PostgreSQL Job Claiming**, only under a separate instruction.
