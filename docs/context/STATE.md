# Work state

Updated: 2026-09-16.

## Current milestone

P1 Persistent Worker Execution + Safe PostgreSQL Job Claiming is complete. The application remains one modular monolith; no AI workload or separate service was added.

## Implemented

- A standard .NET hosted BackgroundService polls without busy-spinning, honors cancellation, creates one scope per job, and is disabled by default.
- Application-owned DTO/contracts define one claimed job, queue persistence operations, and a minimal handler interface.
- PostgreSQL claiming is isolated in Infrastructure. One Read Committed transaction selects one deterministic eligible row with FOR UPDATE SKIP LOCKED and changes it to Running through UPDATE RETURNING.
- Completion, failure, and lease renewal use ownership-guarded updates requiring Job ID, Running status, and Worker ID.
- Worker leases renew while a handler runs. Expired Running jobs are reclaimed when retries remain; recovery consumes one retry. Expired jobs with no retry allowance become Failed.
- Handler failures requeue deterministically while retry allowance remains and preserve concise error details; exhausted jobs become Failed.
- A placeholder handler validates orchestration only. It does not implement AI, research, media, publishing, or analytics work.
- Existing WorkerId and LeaseExpiresAt columns were sufficient, so no migration was created.

## Verification

- PASS - dotnet restore and Release build; 0 warnings/errors.
- PASS - dotnet format verification and git diff whitespace check.
- PASS - 19/19 automated tests.
- PASS - real PostgreSQL concurrent-claim test: two simultaneous claim attempts produced exactly one owner.
- PASS - PostgreSQL claim, Running/Succeeded transitions, retry/exhaustion, lease recovery, and cleanup.
- PASS - API startup and dependency injection; root, liveness, and readiness returned HTTP 200.
- PASS - migration list remains 20260915160805_InitialCoreDomain with no pending schema change.
- NOT RUN - frontend validation because frontend was not changed.

## Known issues and next task

The placeholder handler has no external side effects; future handlers must define their own idempotency strategy. The worker is intentionally disabled by default until a real handler milestone enables it deliberately. Next recommended task: P1 AI Gateway + Ollama integration, only under a separate instruction.
