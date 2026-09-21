# Local AI Ecosystem PRD v0.9.2 — Split Index

**Source:** `Local_AI_Ecosystem_PRD_v0.9.2.md`  
**Status:** Architecture Freeze  
**Source SHA-256:** `71584cd1e1c1b14fc82d7dd3e46b24e67b320abffb8aebf5472471d4cd78e616`

**Post-freeze addendum:** `parts/13_EXTRACTABLE_MODULE_BOUNDARIES.md` (ADR-018 Extractable Module Boundaries, ADR-019 Domain-Specific Workflows and Proven Capability Reuse) was adopted after v0.9.2. It is explicitly *not* derived from the frozen source and does **not** change the source SHA-256 above.

## Purpose

This package splits the frozen v0.9.2 PRD into bounded reading contexts so coding agents do not need to ingest the monolithic document for every task.

Rules for agents:

- Read this index first.
- Read only the smallest relevant part for the task.
- Do not read the full source PRD unless the split part is ambiguous or incomplete for the question being answered.
- The original source remains authoritative if a split file and the source ever differ.
- Split files preserve the original numbered sections and do not add product decisions.
- Post-freeze addenda under `parts/` (currently `13_EXTRACTABLE_MODULE_BOUNDARIES.md`) are explicitly labeled and do not change the frozen source or its checksum.
- Use `execution-context/` for very narrow Video #1 work; those files are concise derived summaries and explicitly call out what v0.9.2 does *not* specify.

## Task → document map

| Task | Read first | Optional second read |
|---|---|---|
| Product goal, scope, success criteria | `parts/01_PRODUCT_STRATEGY_AND_SCOPE.md` | `parts/12_ADR_FREEZE_AND_FINAL_RULES.md` |
| Current architecture, AI boundary, model strategy | `parts/02_CURRENT_ARCHITECTURE_AI_AND_MODELS.md` | `parts/12_ADR_FREEZE_AND_FINAL_RULES.md` |
| Developer AI / Qwen Code | `parts/03_DEVELOPER_AI_TOOLING.md` | `parts/07_ARCHITECTURE_COMPONENT_DECISIONS.md` |
| Durable jobs, cancellation, artifacts, resumability | `parts/04_JOBS_AND_ARTIFACTS.md` | `parts/02_CURRENT_ARCHITECTURE_AI_AND_MODELS.md` |
| Video #1 pipeline | `parts/05_VIDEO1_PRODUCTION_PIPELINE.md` | `parts/10_EXECUTION_ROADMAP.md` |
| Storyboard implementation | `execution-context/STORYBOARD.md` | `parts/04_JOBS_AND_ARTIFACTS.md` |
| Narration / TTS | `execution-context/NARRATION_TTS.md` | `parts/05_VIDEO1_PRODUCTION_PIPELINE.md` |
| Asset collection / creation | `execution-context/ASSETS.md` | `parts/04_JOBS_AND_ARTIFACTS.md` |
| FFmpeg render / QA | `execution-context/RENDERING_QA.md` | `parts/04_JOBS_AND_ARTIFACTS.md` |
| Audience, monetization, analytics, learning | `parts/06_AUDIENCE_REVENUE_LEARNING_AND_REVIEW.md` | `parts/10_EXECUTION_ROADMAP.md` |
| Build/defer/remove architecture choices | `parts/07_ARCHITECTURE_COMPONENT_DECISIONS.md` | `parts/12_ADR_FREEZE_AND_FINAL_RULES.md` |
| Hermes / agent / WSL security | `parts/08_HERMES_AGENT_AND_WSL_SECURITY.md` | `parts/11_REUSABILITY_REMOTE_OPS_AND_FUTURE.md` |
| Publishing, approvals, backups | `parts/09_PUBLISHING_APPROVALS_AND_BACKUPS.md` | `parts/10_EXECUTION_ROADMAP.md` |
| NOW / NEXT / LATER roadmap | `parts/10_EXECUTION_ROADMAP.md` | `parts/12_ADR_FREEZE_AND_FINAL_RULES.md` |
| Reuse gate, Opportunity Hunter, Telegram, reviewer, future agents | `parts/11_REUSABILITY_REMOTE_OPS_AND_FUTURE.md` | `parts/12_ADR_FREEZE_AND_FINAL_RULES.md` |
| Module boundaries, extractable modules, shared capabilities | `parts/13_EXTRACTABLE_MODULE_BOUNDARIES.md` | `parts/12_ADR_FREEZE_AND_FINAL_RULES.md` |
| ADRs, freeze, deferred decisions, execution rules | `parts/12_ADR_FREEZE_AND_FINAL_RULES.md` | none |

## Video #1 shortest PRD path

```text
Audience / Problem / Format / Offer
        ↓
GenerateIdea
        ↓
GenerateScript
        ↓
Manual Script Review
        ↓
Basic Storyboard
        ↓
Collect / Create Assets
        ↓
Narration
        ↓
Subtitle
        ↓
FFmpeg Recipe
        ↓
Final QA
        ↓
VIDEO #1
        ↓
Publish Manually
        ↓
Record Metrics + Human Time
```

This path is sourced from PRD section 47. Heavy generative video and autonomous publishing remain deferred in v0.9.2.

## Package layout

```text
Local_AI_Ecosystem_PRD_v0.9.2_split/
├── PRD_INDEX.md
├── SECTION_MAP.md
├── parts/
├── execution-context/
└── _source/
    └── Local_AI_Ecosystem_PRD_v0.9.2.md
```
