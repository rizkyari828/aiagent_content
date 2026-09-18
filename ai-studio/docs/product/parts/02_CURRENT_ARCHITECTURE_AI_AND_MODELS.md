# 02 Current Architecture Ai And Models

> Derived split of **Local AI Ecosystem PRD v0.9.2**. The numbered sections below are preserved from the frozen source PRD; this file does not introduce new product decisions.

---

# 6. Current Production Architecture

The implementation remains a:

**.NET 10 Modular Monolith**

with:

- PostgreSQL,
- React,
- TypeScript,
- Vite,
- WSL2,
- local filesystem,
- Ollama or compatible local inference,
- FFmpeg,
- faster-whisper where needed.

High-level architecture:

```text
                USER
                  │
                  ▼
             React UI
                  │
                  ▼
       .NET Modular Monolith
                  │
       ┌──────────┼──────────┐
       │          │          │
   Projects     Jobs     Analytics
       │          │          │
       └──────────┼──────────┘
                  │
             PostgreSQL
                  │
                  ▼
               Worker
                  │
       ┌──────────┼───────────────┐
       │          │               │
       ▼          ▼               ▼
 IAiTextGenerator FFmpeg      Local Tools
       │
       ▼
 Ollama / Local Model
       │
       ▼
    Artifacts

```

There is currently:

- no Agent Platform deployment,
- no mandatory Agent Runtime,
- no Agent Gateway,
- no LeadAgent,
- no dynamic Agent Registry.

The Content Studio must remain usable if any future Agent Runtime is unavailable.

Research may be supplied manually, scripts may be edited manually, and production may continue from approved application-owned inputs without Hermes or another orchestration runtime.

---

# 23. AI Integration

The primary text AI abstraction remains:

```text
IAiTextGenerator

```

Do not introduce a broader generic `IModelProvider` unless an actual new requirement makes `IAiTextGenerator` insufficient.

Example:

```text
GenerateIdeaJobHandler
        ↓
IAiTextGenerator
        ↓
OllamaTextGenerator
        ↓
Qwen

```

Keep provider configuration close to the adapter.

## Product Boundary for AI Results

Anything crossing from AI into application state must use a task-specific validated result.

A minimum result should contain, where relevant:

```text
Job / Project identity
Task kind
Input artifact versions
Result schema version
Completed / Incomplete / Blocked status
Output
Evidence references
Unresolved questions
Model / prompt version
Validation errors
Duration
```

Do not build a universal inter-agent protocol.

Structured output is required where software consumes the result, but valid JSON proves structure only, not factual correctness.

Avoid treating arbitrary model-generated confidence values such as `0.86` as calibrated truth.

Prefer:

```text
What claims are supported?
Which sources are primary?
What remains unverified?
What contradicts the conclusion?
What evidence would change the recommendation?
```

---

# 24. AI Gateway

“AI Gateway” remains a conceptual integration boundary, not necessarily a network service.

Initially it may simply be:

```text
Application
    ↓
IAiTextGenerator
    ↓
Provider Adapter

```

Do not build:

- capability routing,
- distributed provider discovery,
- model registry service,
- generic AI protocols,

while only one or two providers exist.

---

# 25. Model Strategy

Hardware:

**RTX 4060 Ti 16 GB**

Initial policy:

```text
1 primary model
+
1 optional stronger model only when justified
+
specialized models loaded on demand

```

Only one substantial GPU workload should normally run at a time.

Measure:

- VRAM,
- RAM,
- offload,
- accepted-output latency,
- retries,
- model load/unload time,
- disk use.

Do not choose models based solely on benchmark prestige.

---
