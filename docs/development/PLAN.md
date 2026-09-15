# Incremental implementation plan

Status: working breakdown of PRD v0.7; all development slices pending. This orders delivery without removing P1 requirements or promising an unbenchmarked schedule.

## Immediate scope

DOC-01: repo agent guide, compact context, source PRD, architecture, backlog, and Figma handoff. No application scaffold is part of this documentation task.

## Pilot track — PRD P0

P0-01 inventory/benchmark → P0-02 topic/evidence → P0-03 original demo/script/recordings → P0-04 render/QA/package → P0-05 manual publication/logs. Pilot can use manual tools and portable files; production activity and publication are not claimed complete by development work.

## Development slices

| ID | Scope and dependency | Evidence of completion |
| --- | --- | --- |
| DEV-01 | Foundation: inspect tools, .NET API, PostgreSQL migrations, configured filesystem, project create/read/update; independent of Figma | Reproducible setup/run instructions; project survives host restart; invalid input handled; config has no secrets |
| DEV-02 | Brief/research/script versions, local AI gateway, A1/A2; after DEV-01 | Local input/output traceable; invalid output editable; no cloud fallback; changed script invalidates affected approval |
| DEV-03 | Local asset/narration/subtitle import, metadata/hashes/eligibility; after DEV-01 | Valid media readable; traversal/bad media rejected; unresolved rights cannot pass publication QA; subtitles editable |
| DEV-04 | Durable jobs and one-template manifest renderer; after DEV-02/03 | Audio/video/subtitle output reviewable; failed job retries safely; restart/lease recovery and cancel verified; resources serialized |
| DEV-05 | QA, A3, export package, manual publication record; after DEV-04 | Critical failure or stale A3 blocks readiness; preview/export works; real publication distinguished from local export |
| DEV-06 | Metrics/time/cost/revenue and review notes; after DEV-01, integrate publication when present | Null vs zero, cumulative snapshots, actual/estimated cash, fees/refunds, and overlapping human time handled correctly |
| DEV-07 | Project export/import and recovery/offline runbook; after data/artifact slices | Fresh-location/database restore retains references and files; licensed omissions explicit; cached-input render succeeds offline |
| UI-01 | React foundation and Figma integration per completed slice | Screens match supplied frames; forms/API/errors/job states work; empty/loading/error states and keyboard access verified |

Dependencies allow backend work during design. UI integration is needed for browser-facing acceptance, even when backend slices already pass. Establish portable file/schema conventions early so pilot imports and DEV-07 do not require reconstruction.

## PRD acceptance traceability

| PRD criteria | Delivery coverage |
| --- | --- |
| AC-01 persistent browser project | DEV-01 + UI-01 |
| AC-02 local text AI | DEV-02 |
| AC-03 sources/script/A1/A2 | DEV-02 + UI-01 |
| AC-04 media/rights/subtitles | DEV-03 + UI-01 |
| AC-05 validated render | DEV-04 + UI-01 |
| AC-06 job recovery | DEV-04 |
| AC-07 QA and current A3 | DEV-05 + UI-01 |
| AC-08 export and real video ID | DEV-05 + actual pilot/publication |
| AC-09 time/cost/metrics | DEV-04/06 + UI-01 |
| AC-10 project portability | DEV-07 |
| AC-11 offline cached-input production | DEV-02/03/04/07 |
| AC-12 no required new payments | All slices |

P1 is complete only when all twelve criteria hold for one real project. API smoke tests alone do not prove the full production workflow.

## Working limits

- Preserve the PRD's eight human hours/week and 12 pre-pilot development hours; no silent estimate that all P1 fits those hours.
- Track development effort separately from content work. Prioritize repeated bottlenecks with estimated minutes saved per use.
- Keep P2/P3 and conditional integrations in the PRD roadmap. Do not pull them into P1 to fill missing design time.
- At slice completion update STATE with actual files, commands, outcomes, and next task. Introduce only checks justified by behavior and risk.
