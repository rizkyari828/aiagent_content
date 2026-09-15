# Architecture baseline

Status: intended design from PRD v0.7, not implemented. Read PRD §§11–19 and §§24–26 for full contracts.

## Runtime boundaries

```text
Browser (React + TypeScript + Vite)
  → local .NET 10 API / application modules
      → PostgreSQL: metadata, versions, approvals, durable jobs
      → local filesystem: media and project artifacts
      → one worker: Ollama / Python transcription / FFmpeg
  ← preview, errors, job status, export package
Operator → YouTube Studio manually → record publication ID
```

Modules: Content, Research, AI, Assets, Production, Publishing, Measurement. Use internal interfaces and small adapters. Begin with folders where separate assemblies add no value; no microservices or queue broker required. Python is a media runner, not a second general application backend.

Proposed source layout when scaffolding starts:

```text
src/AIStudio.Api/       # API, host, initially folder-based application/domain/infrastructure
src/AIStudio.Web/       # React UI, aligned to user Figma
media/                 # transcription/media runners when needed
tests/                 # behavior/integration checks as features land
docs/                  # specification, current context, decisions, runbooks
data/                  # ignored runtime root
```

## Data and lifecycle

- Add entities per slice. PRD entities: Content, ResearchSource/Claim, ScriptVersion, Asset/AssetUsage, RenderManifest, Approval, Job, Publication, MetricObservation, TimeEntry, CostEntry, RevenueEntry, ExperimentNote.
- Store media on disk using paths relative to a configured root. Database holds metadata/references and selected JSONB fields; no media blobs.
- Content lifecycle: Draft → Researching → IdeaReview → Scripting → ScriptReview → Producing → FinalReview → ReadyToPublish → Published; Archived is separate. Define allowed transitions and revisions as implemented; do not assume a generic status setter is sufficient.
- Jobs: Queued, Running, Succeeded, Failed, Cancelled. Atomic claim/lease, reconciliation after expiry, input hashes, bounded retry, output reuse, controlled child-process cancellation. GPU-heavy concurrency one.
- A1 references brief/angle; A2 script/claims; A3 rendered artifact and publication metadata. Material edits invalidate affected approvals/outputs. Retry alone does not invalidate unchanged inputs.
- Unknown metrics are null with reason, not zero. Snapshots are not summed. Money has explicit currency and actual/estimated/refunded distinctions; human and machine time stay separate.

## Contracts and validation

- Text generation adapter: local endpoint allowlist, prompt/model/input references, bounded context, timeout, status/duration, schema validation, max two transient retries. No unlimited agent loop.
- Manifest schema: version, content/script version, output settings, asset references, audio/subtitles, ordered scenes, source offsets, durations, captions. Validate bounds, gaps/overlaps, hashes, media eligibility, and audio/video alignment before execution.
- Render baseline: MP4 H.264/AAC, 1920×1080, 30 FPS; cheaper preview allowed. Start with a repeatable template. Save tool/config/font versions and input hashes; no claim of identical bytes across environments.
- Technical QA + human review guard ReadyToPublish. Publication requires recorded confirmation; generating an export is not publishing.
- Export/import must retain recoverable project relationships and licensed files/references, exclude credentials, and be verified in a fresh location/database. Offline operation is tested after dependencies/models are available.
- Loopback services; validated paths/media/schemas; structured process arguments. External content is untrusted data. Keep secrets out of logs, exports, Git, and demo material.

## Decisions still open

Exact dependency pins, package manager, API routes/DTOs, ports, migration tooling, production OS, available accelerators, and model configuration are selected during the relevant slice. Figma does not define database entities or override domain safeguards. No unmeasured hardware performance is assumed.
