# Work state

Updated: 2026-09-16.

## Current milestone

P1 GenerateIdea Job Handler + First AI Vertical Slice is complete. The modular monolith now has one real AI workload path; GenerateScript and all later workflows remain deferred.

## Implemented

- `GenerateIdea` uses a small JSON payload containing project ID, topic, optional audience/language, and optional provider-neutral model override.
- The Application handler loads a focused ContentProject projection, builds a provider-neutral prompt, and calls `IAiTextGenerator` with JSON-object output.
- AI output is validated against title, hook, summary, angle, targetAudience, and suggestedFormat, then serialized canonically.
- The existing worker owns claim, completion, retry, and failure transitions. Typed handler errors preserve useful error codes without duplicating retry logic.
- Successful output is persisted and queryable through the existing `Job.Result` JSONB column; no Idea table or migration was added.
- Infrastructure DI resolves exactly one GenerateIdea handler; the placeholder no longer claims that job type.

## Verification

- PASS - Release build; 0 warnings/errors.
- PASS - 12/12 targeted GenerateIdea tests, including DI, payload/schema, prompt/model mapping, errors, and cancellation.
- PASS - 4/4 affected worker lifecycle tests.
- PASS - real PostgreSQL worker pipeline with fake AI persisted and queried structured Job.Result; test rows cleaned.
- PASS - API startup/DI; root, liveness, and readiness returned HTTP 200.
- PASS - format and whitespace checks.
- NOT AVAILABLE - live Ollama generation because Ollama CLI/API is not installed or reachable in current WSL.
- NOT RUN - migration checks, unrelated PostgreSQL concurrency suite, frontend, and dependency audit because those areas were unchanged.

## Known issues and next task

Live `gemma3:4b` generation remains unverified, and there is not yet an API command/endpoint to enqueue GenerateIdea jobs. Next recommended task: P1 GenerateIdea enqueue/query API vertical slice, under separate instruction; GenerateScript remains out of scope.
