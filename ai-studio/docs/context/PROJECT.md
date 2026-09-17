# Project context

Updated: 2026-09-15. Derived summary; canonical requirements: PRD v0.7, indexed in [INDEX](../product/INDEX.md).

## User intent

- Build Local AI Content Studio. Foundation, local runtime, and Core Domain + Persistent Job Model are implemented; the next worker milestone requires a separate instruction.
- User designs Figma in parallel. No design URL, frames, or tokens received yet.
- Prior review suggested smaller delivery slices. Keep PRD acceptance criteria intact; the review did not replace the PRD or select a new audience.

## Product baseline

- Internal studio for one operator/YouTube channel; Indonesian faceless demos, 5-8 minutes, 1080p/30 FPS.
- Audience hypothesis: beginner developers and technical workers using local AI and document/file automation. Demonstrate actual results and limitations.
- Pipeline: brief/research -> A1 idea approval -> versioned script -> A2 script approval -> local media/narration/subtitles -> scene manifest -> render/QA -> A3 final approval -> export/manual upload -> record video ID/metrics.
- Additional product budget Rp0; record human time, machine time, and costs separately. No paid/cloud AI fallback. Existing hardware is a prerequisite, not yet benchmarked.
- Pilot target: publish within three weeks of kickoff; max 12 pre-pilot development hours. Overall allocation: eight human hours/week; after pilot, around six content/business and two software. Kickoff date is not recorded.
- Test small workflow/template sales only with demand evidence. Rp49,000 and three buyers are experiment targets, not validated economics. Long-term target: positive cash surplus for three consecutive months and at least Rp50,000 per total human hour.

## Technical baseline

Content Studio is rooted at `ai-studio/` in the monorepo. The sibling `local-ai-infra/` solution is reusable runtime tooling, not an application runtime dependency.

.NET 10 modular monolith + React/TypeScript/Vite + PostgreSQL metadata + filesystem media. Maintain explicit module/contracts boundaries so demonstrated resource-heavy workloads can later become specialized workers; P1 remains one core deployment without distributed-system infrastructure. Ollama/Qwen3-8B quantized candidate; recorded human narration; faster-whisper multilingual small candidate; validated manifests rendered with FFmpeg. Actual model digests, hardware suitability, and licenses still need implementation-time verification.

Default development is Windows host/browser plus WSL2-hosted source, .NET, Node, API, and Web. Persistent tooling is .NET SDK 10.0.401 and Node.js 24.17.0 LTS/npm 11.13.0. Docker Desktop supplies the WSL-integrated engine; `compose.yaml` in `ai-studio/` provides PostgreSQL 18.6 with environment credentials, loopback exposure, and a named volume.

Implemented persistence includes ContentProject and Job with application-generated UUIDs, UTC timestamps, string lifecycle states, JSONB job payload/result, bounded retries, minimal lease metadata, explicit EF mappings, and a real migration. ContentItem is deferred until a separate persisted content artifact is required.

One persistent PostgreSQL job worker may later be hosted as a BackgroundService; no worker loop or atomic claiming is implemented yet. GPU-heavy concurrency starts at one. Three version-bound approvals; critical QA failures block publication readiness. Export/import and restore remain P1 requirements. Available local inputs must support offline production after setup.

Deferred: TTS activation, external asset providers/search, image/video generation, upload/analytics APIs, RabbitMQ, Valkey, pgvector, complex timeline editing, multi-channel/SaaS, autonomous publishing.

## Read only what the task needs

| Task | Read next | PRD sections if detail needed |
| --- | --- | --- |
| Current work | [CHECKPOINT](../agent/CHECKPOINT.md), then [STATE](STATE.md) | - |
| Delivery/acceptance | [PLAN](../development/PLAN.md) | 9-10, 27-30 |
| Backend/data/API | [ARCHITECTURE](../development/ARCHITECTURE.md) | 11, 13-15, 24-26 |
| UI integration | [FIGMA-HANDOFF](../design/FIGMA-HANDOFF.md) | 11-12, 19 |
| AI/voice/media/render | Relevant PRD sections | 3-4, 14, 16-19, 24 |
| Metrics/business | Relevant PRD sections | 5-8, 20-23, 29 |
| Decisions/change | [DECISIONS](../development/DECISIONS.md) | 32 |

This is a maintained file summary, not an inference/prompt cache. Reconcile it with current user instructions and repository evidence; consult the indexed source when uncertain.
