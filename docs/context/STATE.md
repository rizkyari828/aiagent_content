# Work state

Updated: 2026-09-17.

## Current milestone

GenerateScript is implemented as the second AI vertical slice after GenerateIdea. The next PRD step is manual script review; storyboard and later production stages remain deferred.

## Implemented

- `POST /api/content-projects/{contentProjectId}/generate-script-jobs` enqueues `JobType.GenerateScript` from a selected structured idea.
- The handler uses `IAiTextGenerator` with JSON-object output, validates title, opening hook, ordered narration sections, and closing, then persists canonical JSON in `Job.Result`.
- `GET /api/jobs/{jobId}` returns typed GenerateIdea or GenerateScript results without exposing worker/lease internals.
- GenerateScript uses the configured Studio runtime model because its request leaves the optional per-job model unset.
- Existing PostgreSQL claiming, ownership, lease, retry, and recovery semantics are unchanged; no migration was added.

## Verification

- PASS - 39/39 focused GenerateScript, GenerateIdea regression, and worker tests.
- PASS - Release API build with 0 warnings/errors on .NET SDK 10.0.401.
- PASS - format and whitespace checks.
- UNAVAILABLE - PostgreSQL fake-AI vertical test; local PostgreSQL refused connection and Docker CLI/WSL integration was unavailable.
- NOT RUN - real 27B GenerateScript smoke; unit paths use fake AI and the configured qwen3.8 runtime was already validated on GenerateIdea.

## Known issues and next task

Implement the smallest manual script review/edit milestone before storyboard generation.
