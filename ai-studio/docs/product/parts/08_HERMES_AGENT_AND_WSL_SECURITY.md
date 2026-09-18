# 08 Hermes Agent And Wsl Security

> Derived split of **Local AI Ecosystem PRD v0.9.2**. The numbered sections below are preserved from the frozen source PRD; this file does not introduce new product decisions.

---

# 38. Hermes Status

Hermes remains a valid future candidate.

Status:

**DEFERRED OPTIONAL BOUNDED AGENT RUNTIME**

Hermes is not the manager, workflow engine, source of truth, or business authority of Content Studio.

If adopted later, it executes bounded reasoning/tool tasks requested by Content Studio and returns results for application validation.

Hermes is not required for:

- GenerateIdea,
- GenerateScript,
- storyboard generation,
- rendering,
- publishing,
- basic AI Chat,
- local coding assistance.

---

# 39. When Hermes May Be Reconsidered

Revisit Hermes only when a recurring workflow requires adaptive reasoning and tool selection and direct model calls are no longer sufficient.

The first likely candidate is bounded adaptive research, not general studio management.

Do not adopt Hermes merely to create named Agent roles, persistent personalities, Telegram personas, or an organizational chart.

Candidate:

```text
Research question
    ↓
Read source
    ↓
Determine missing evidence
    ↓
Choose next source
    ↓
Resolve conflict
    ↓
Produce sourced conclusion

```

This differs from deterministic:

```text
Idea
↓
Script
↓
Storyboard
↓
Render

```

---

# 40. Future Hermes Spike

Entry condition:

> Adaptive research is a measured human-time bottleneck.

Hard scope:

- bounded research task,
- public/synthetic input,
- read-only tools,
- no production credentials.

Initial permissions:

```text
ALLOW:
content.search
source.read
local inference

DENY:
raw shell
arbitrary filesystem
publishing
MCP
cron
delegation
Skill mutation
memory mutation
Docker socket
production DB credentials

```

Hermes must demonstrate measurable time savings before adoption.

Compare it against the existing direct-call baseline using representative tasks.

Measure:

```text
Accepted Output / Operator Hour
Human Review + Correction Time
Failure / Recovery Time
Material Error Rate
Inference / GPU Time
Latency
Maintenance Effort
```

If schema repair, tool failures, review, or runtime maintenance erase the saving, continue without Hermes.

A second Agent is not justified merely because the first Agent exists.

---

# 41. Future Hermes Security Boundary

A future Agent Runtime is treated as untrusted relative to business data.

Conceptual future boundary:

```text
Windows
   │
restricted integration
   ▼
WSL2
   │
   ▼
Contained Hermes Runtime
   │
   ├── Read-only approved input
   ├── bounded writable output
   └── restricted network
         │
         ▼
   local inference endpoint

```

Important credentials and business writes remain outside Agent authority.

Hermes Profiles, identities, memories, or fresh delegated contexts are not security boundaries.

Security isolation must be enforced by the operating environment, filesystem/network restrictions, application capabilities, credentials, and process/container boundaries.

---

# 42. WSL Security

WSL2 is useful isolation, but it is not treated as an absolute Windows security boundary.

Required posture:

- dedicated environment where practical,
- non-root workloads,
- no unnecessary Windows automount,
- no unnecessary interoperability,
- restricted files,
- private service bindings,
- careful network exposure,
- recoverable backups.

Unknown malicious executable code should not be run in the production AI environment.

---
