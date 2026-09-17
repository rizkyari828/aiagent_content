# Work state

Updated: 2026-09-17.

## Current milestone

The repository layout migration is complete: Content Studio is rooted at `ai-studio/`, reusable local AI tooling remains at `local-ai-infra/`, and application behavior is unchanged.

## Implemented

- Moved the Studio solution, source, tests, product documentation, SDK/tool pins, Compose configuration, environment example, local environment file, Qwen guidance, and generated test output under `ai-studio/`.
- Added concise root monorepo routing in `AGENTS.md` and `README.md`; Studio-specific guidance remains with the product.
- Preserved GenerateIdea, GenerateScript, durable-job behavior, API contracts, migrations, namespaces, and project names without source changes.
- Preserved the untracked `.qwen/settings.json` and `docs/Local_AI_Ecosystem_PRD_v0.9.2.md` under `ai-studio/`; neither is staged.
- No nested Git repository exists and `local-ai-infra/` behavior was not changed.

## Verification

- PASS — `dotnet build src/AIStudio.Api/AIStudio.Api.csproj --configuration Release --no-restore`: 0 warnings, 0 errors on .NET SDK 10.0.401.
- PASS — `dotnet sln AIStudio.slnx list`: all five project references resolve from `ai-studio/`.
- PASS — focused GenerateIdea, GenerateScript, and worker unit/fake-AI tests excluding optional persistence integration: 45/45.
- PASS — `python3 -m unittest discover -s tests -v` in `local-ai-infra/`: 3/3.
- PASS — nested-Git, protected-file presence, stale absolute-path, and `git diff --check` checks.
- ENVIRONMENT UNAVAILABLE — Docker CLI/WSL integration, so `docker compose config` could not run. Static inspection found no build context or relative file path in the PostgreSQL-only Compose file.
- ENVIRONMENT UNAVAILABLE — an initial broad test filter selected two optional PostgreSQL vertical tests; 45 tests passed and those two stopped because `ConnectionStrings__DefaultConnection` was unset. No repository defect was indicated.

## Known issues and next task

Implement the smallest manual script review/edit milestone before storyboard generation.
