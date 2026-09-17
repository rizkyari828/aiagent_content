# Architecture baseline

Status: P1 Foundation, local PostgreSQL runtime, Core Domain, and Persistent Job Model implemented; later product modules remain intended design. Read PRD §§11–19 and §§24–26 for full contracts.

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

Modules: Content, Research, AI, Assets, Production, Publishing, Measurement. The initial deployment is a modular monolith with explicit module boundaries and contracts. The intended evolution is **Modular Core + Specialized Workers**: resource-intensive or independently scalable workloads may later be extracted without turning the core product into distributed infrastructure prematurely.

Likely extraction candidates are AI inference/Ollama, research/crawling, TTS, Whisper, image/video generation, FFmpeg rendering, media processing, publishing workers, and analytics ingestion. Keep orchestration and product rules in the modular core; isolate workload adapters behind application-owned contracts and pass serializable, versioned inputs rather than sharing infrastructure implementation details.

P1 remains a single deployable core. Do not add RabbitMQ, Kafka, Redis messaging, service discovery, an API gateway, Kubernetes, per-module databases, distributed tracing, or microservice networking without demonstrated need. PostgreSQL and in-process calls are the initial coordination mechanisms. Python remains a bounded media runner, not a second general application backend.

Implemented source layout:

```text
src/AIStudio.Api/       # ASP.NET Core composition root
src/AIStudio.Web/       # React UI, aligned to user Figma
src/AIStudio.Application/ # Use cases and core-owned contracts
src/AIStudio.Domain/    # Domain rules and models
src/AIStudio.Infrastructure/ # PostgreSQL/EF Core and external adapters
media/                 # transcription/media runners when needed
tests/                 # behavior/integration checks as features land
docs/                  # specification, current context, decisions, runbooks
data/                  # ignored runtime root
```

## Local development topology

Windows hosts the browser and Docker Desktop. Source code, .NET/Node tooling, API, and Web run inside WSL2. Docker Desktop exposes its WSL2-backed engine to the distribution, and development containers must be reachable from WSL.

PostgreSQL development uses the root Compose file with official PostgreSQL 18.6 Alpine, environment-provided credentials/port, and named volume aistudio-postgres-data mounted at /var/lib/postgresql. It is the only development container.

## Data and lifecycle

- Add entities per slice. ContentProject and Job are the first implemented roots; ContentItem remains deferred until a separate persisted artifact is required. Other PRD entities remain future slice work.
- EF Core and Npgsql are isolated in Infrastructure. ApplicationDbContext exposes the two implemented sets, and migration 20260915160805_InitialCoreDomain creates only their real schema plus EF migration history.
- Store media on disk using paths relative to a configured root. Database holds metadata/references and selected JSONB fields; no media blobs.
- Content lifecycle: Draft → Researching → IdeaReview → Scripting → ScriptReview → Producing → FinalReview → ReadyToPublish → Published; Archived is separate. Define allowed transitions and revisions as implemented; do not assume a generic status setter is sufficient.
- Jobs: Queued, Running, Succeeded, Failed, Cancelled. The persistent model includes input hashes, bounded retry, JSONB input/output, and minimal worker/lease fields. Atomic claiming, lease reconciliation, execution, output reuse, and cancellation remain the next milestone.
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

API routes/DTOs, production OS, available accelerators, and model configuration are selected during the relevant slice. Figma does not define database entities or override domain safeguards. No unmeasured hardware performance is assumed.
