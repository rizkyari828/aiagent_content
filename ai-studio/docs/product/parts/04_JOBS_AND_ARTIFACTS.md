# 04 Jobs And Artifacts

> Derived split of **Local AI Ecosystem PRD v0.9.2**. The numbered sections below are preserved from the frozen source PRD; this file does not introduce new product decisions.

---

# 16. PostgreSQL Jobs

PostgreSQL remains the single durable execution authority.

Content Studio application code owns authoritative workflow transitions.

Agent runtimes, cron systems, messaging bots, or external workflow tools must not become competing production-state authorities.

A future agent may propose a priority, plan, draft, or action request.

It does not authoritatively move a project between production states unless Content Studio validates and performs that transition.

Flow:

```text
Application
    ↓
Create Job
    ↓
PostgreSQL
    ↓
Worker
    ↓
Handler
    ↓
AI / Tool
    ↓
Validated Result
    ↓
PostgreSQL

```

Assume:

> at-least-once execution.

Do not assume exactly-once behavior.

---

# 17. Job Execution Guarantees

Before expensive unattended workloads, Jobs must support clear semantics for:

- execution ownership,
- retries,
- cancellation,
- abandoned executions,
- stale workers,
- artifact promotion.

A Job execution should conceptually have:

```text
JobId
Status
AttemptId
Owner
StartedAt
Heartbeat / Lease if needed
RetryCount
CancellationRequested
CompletedAt
Error

```

A stale attempt must never overwrite a newer accepted attempt.

---

# 18. Job Claiming

Job claiming must use a short database transaction.

Do not keep a DB transaction open during:

- LLM inference,
- image generation,
- rendering,
- transcription,
- external API work.

For multiple workers, PostgreSQL queue-compatible locking patterns may be used where required.

The initial implementation may remain single-worker if sufficient.

---

# 19. Cancellation

Cancellation consists of:

```text
Cancellation Requested
       ↓
Execution receives signal
       ↓
External/subprocess work stops
       ↓
Stop confirmed
       ↓
Job = Cancelled

```

Setting a database flag alone does not mean work has stopped.

Subprocess-based work must have a termination strategy.

---

# 20. Artifact Lifecycle

Production outputs become lightweight artifacts.

Possible types:

```text
Idea
ResearchPackage
Script
Storyboard
SceneManifest
Image
VoiceTrack
Subtitle
VideoShot
RenderManifest
FinalVideo

```

Artifact metadata should minimally include:

```text
ArtifactId
ProjectId
Type
Version
Path
CreatedAt
ProducingJobId
AttemptId
InputHash
OutputHash
ByteSize
ValidationStatus
ParentArtifactIds
ModelId / ToolVersion
Metadata

```

For externally sourced assets also record when relevant:

```text
Source
Creator
License / Rights
RetrievedAt

```

---

# 21. Artifact Promotion

Never directly write a final accepted output.

Preferred process:

```text
Generate
   ↓
Temporary Output
   ↓
Validate
   ↓
Calculate Integrity Hash
   ↓
Atomic Rename / Promotion
   ↓
Register Completed Artifact

```

A file merely existing does not prove it is valid.

For video, validate basic media integrity such as:

- expected duration,
- presence of video stream,
- presence of required audio,
- successful decoding/probing.

---

# 22. Artifact Resumability

Expensive workflows should resume at completed stages.

Example:

```text
Script       DONE
Storyboard   DONE
Scene 1      DONE
Scene 2      DONE
Scene 3      FAILED
Scene 4      PENDING

```

Resume:

```text
Scene 3

```

Do not regenerate successful upstream work unless its input hash changed.

---
