# Video #1 Execution Context — Storyboard

**Derived from v0.9.2 sections:** 22, 26, 27, 35, 47.  
**Purpose:** bounded context for Storyboard work. No new requirements are introduced here.

## What v0.9.2 requires

- Storyboard is part of the deterministic content pipeline between Script and Scene Manifest.
- For Video #1, Storyboard is a **simple structured draft**.
- `Storyboard generation` and `Scene Manifest` are explicitly in **KEEP VERY SIMPLE**.
- The NOW roadmap requires **Basic Storyboard** before asset collection/creation.
- Completed expensive stages should be resumable; successful upstream work should not be regenerated unless its input hash changed.

## What v0.9.2 does not specify

The PRD does **not** define the Storyboard JSON schema, field names, persistence table, HTTP contract, prompt wording, or exact validation rules.

Those implementation details must follow the current codebase patterns and frozen architecture rather than being invented as PRD requirements.

## Minimal reading path

1. This file.
2. `../parts/04_JOBS_AND_ARTIFACTS.md` if durable job/artifact semantics matter.
3. `../parts/02_CURRENT_ARCHITECTURE_AI_AND_MODELS.md` if AI result boundaries matter.
4. Original PRD only if ambiguity remains.
