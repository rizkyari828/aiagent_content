# 12 Adr Freeze And Final Rules

> Derived split of **Local AI Ecosystem PRD v0.9.2**. The numbered sections below are preserved from the frozen source PRD; this file does not introduce new product decisions.

---

# 53. ADR Candidates

Future implementation decisions should move to ADRs instead of repeatedly changing the PRD.

Recommended:

```text
ADR-001 .NET Modular Monolith
ADR-002 PostgreSQL Durable Jobs
ADR-003 Local-First AI
ADR-004 IAiTextGenerator Boundary
ADR-005 Job Attempt Ownership
ADR-006 Artifact Promotion
ADR-007 WSL2 Security Boundary
ADR-008 Qwen Code Developer Tooling
ADR-009 Deterministic Workflow First
ADR-010 Hermes Adoption Criteria
ADR-011 FFmpeg Composition
ADR-012 Manual Publishing Initially
ADR-013 Rp0 Pre-Validation Added Budget
ADR-014 Application-Owned Knowledge and Correction Capture
ADR-015 Optional Agent Adoption Gate
ADR-016 Reusable Capability Extraction Gate
ADR-017 Telegram as Secondary Operations Surface

```

---

# 54. Architecture Freeze

The following decisions are now frozen:

```text
.NET 10 Modular Monolith

PostgreSQL as business state authority

PostgreSQL Jobs as durable production execution authority

IAiTextGenerator as current local text-AI boundary

Deterministic workflow before Agent orchestration

Local artifacts with integrity/provenance metadata

Human approval before publishing

FFmpeg as initial composition engine

Qwen Code as independent developer tooling

Hermes deferred until measured need

No Agent Platform before initial content validation

No RAG/vector infrastructure before measured retrieval need

No microservice decomposition without operational justification

Application-owned workflow state; no Agent as competing authority

Application-owned knowledge; Agent memory is optional and non-authoritative

Agent adoption only after measured bottleneck

Reusable capability extraction only after a second real workflow demonstrates duplication

Telegram remains optional/secondary

```

---

# 55. What Can Break the Freeze

Architecture Freeze may only be broken by:

- demonstrated security risk,
- blocking production failure,
- invalid architectural assumption,
- strong operational evidence,
- major business evidence,
- or a significantly simpler proven architecture.

A new library, framework, model, or Agent project being interesting is not sufficient.

---

# 56. Decisions Intentionally Deferred

These are not architectural gaps.

They are deliberately unresolved:

- Hermes final adoption,
- Agent topology,
- Skill system,
- private Agent memory,
- persistent specialist profiles,
- Lead/Strategy Agent topology,
- RAG,
- embedding model,
- vector search,
- image model,
- video model,
- TTS model,
- AI Chat implementation,
- external hosting,
- multi-user infrastructure.

They will be decided through evidence.

---

# 57. Final Execution Rule

Before building any significant subsystem ask:

```text
Does this directly help us:

publish?
improve quality?
reduce recurring human work?
protect important data?
test willingness to pay?
increase sustainable revenue?

```

If no:

```text
DEFER

```

If maybe:

```text
MEASURE FIRST

```

If yes:

```text
BUILD THE SMALLEST VERSION

```

---

# 58. Final Principle

```text
Less infrastructure
More real output

Less theoretical autonomy
More reliable automation

Less framework building
More publishing

Less model chasing
More accepted output

Less future-proofing
More present validation

Less engineering vanity
More audience evidence

Less quota waste
More effective developer time

Less automation for its own sake
More sustainable profit

```

The project does not win by having the most sophisticated local AI architecture.

It wins if one developer can use local AI to build faster, repeatedly publish useful work, learn from real audiences, convert some of that value into revenue, and expand automation only where the evidence says it is worth the cost.

**v0.9.2 is the Architecture Freeze baseline.**
