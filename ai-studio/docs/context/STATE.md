# Work state

Updated: 2026-09-17.

## Current milestone

Manual Script Review/Edit is complete. The next product slice is Basic Storyboard generation from an explicitly approved reviewed script.

## Implemented

- Completed GenerateScript `Job.Result` remains immutable historical AI evidence.
- One `ReviewedScript` per ContentProject stores the canonical human-reviewed GenerateScript contract in JSONB and retains `SourceJobId` provenance.
- Lifecycle is only `Draft -> Approved`; canonical content changes increment `Revision`, identical edits are no-ops, approval is idempotent, approved scripts cannot be edited, and a different source job cannot silently replace the current script.
- HTTP supports starting review from a completed GenerateScript job, retrieving the current reviewed script, editing it, and approving it. No reject state or generic approval framework was added.
- Migration `20260917113100_AddReviewedScripts` is additive and does not alter Job persistence or durable-job semantics.

## Verification

- PASS — 58/58 focused Script Review, GenerateScript, GenerateIdea, API contract, DI, EF model, and worker tests; optional PostgreSQL vertical tests were excluded.
- PASS — Release API build on .NET SDK 10.0.401 with 0 warnings and 0 errors.
- PASS — `dotnet format AIStudio.slnx --verify-no-changes --no-restore`.
- PASS — EF reports no pending model changes after `AddReviewedScripts`; migration SQL generation contains only the new table, constraints, foreign keys, indexes, and migration-history insert.
- UNAVAILABLE — applying the migration and DB-backed verification: PostgreSQL connection to `127.0.0.1:5432` was refused and Docker Desktop WSL integration was unavailable.
- NOT RUN — real 27B inference; review behavior reuses persisted structured output and required no model call.

## Known issues and next task

Implement the smallest Basic Storyboard slice, accepting only the canonical `ReviewedScript` whose status is `Approved`.
