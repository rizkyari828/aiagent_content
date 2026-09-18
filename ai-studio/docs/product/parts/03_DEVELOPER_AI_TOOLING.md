# 03 Developer Ai Tooling

> Derived split of **Local AI Ecosystem PRD v0.9.2**. The numbered sections below are preserved from the frozen source PRD; this file does not introduce new product decisions.

---

# 7. Existing Working Vertical Slice

The current working flow is preserved:

```text
ContentProject
      ↓
GenerateIdea Job
      ↓
PostgreSQL Worker
      ↓
GenerateIdeaJobHandler
      ↓
IAiTextGenerator
      ↓
Local AI Provider
      ↓
Structured Result
      ↓
Job.Result JSONB

```

This architecture is considered the baseline to extend.

Do not rewrite working code merely to match conceptual architecture.

---

# 8. Local Developer AI Bootstrap

Developer productivity is an immediate operational requirement because hosted coding-agent quota interruptions already occur during development.

Therefore a small pre-development bootstrap is approved.

## Phase 0 — Local Developer AI Bootstrap

Purpose:

> Reduce dependence on hosted coding-agent quota and allow development to continue using local AI.

This is:

**developer tooling**

and explicitly not:

- Content Studio architecture,
- Agent Platform,
- production infrastructure,
- or a product dependency.

Architecture:

```text
DEVELOPER ENVIRONMENT

        Development Task
               │
       ┌───────┴────────┐
       │                │
       ▼                ▼
   Qwen Code          Codex
       │
       ▼
Local Qwen Model


----------------------------------
       PRODUCT BOUNDARY
----------------------------------

Content Studio
      ↓
PostgreSQL Jobs
      ↓
IAiTextGenerator
      ↓
Local Content Model

```

---

# 9. Developer AI Time Box

Target setup effort:

**90 minutes**

Hard maximum active effort:

**3 hours**

Passive model downloads do not count as active debugging time.

If usable Local Developer AI cannot be established within the hard time box:

> Stop.

Do not respond by building another framework.

Continue Content Studio development with the available tools.

---

# 10. Developer AI Minimum Scope

Required:

```text
WSL2
 ↓
Existing Local Inference Runtime
 ↓
One Existing Qwen Model
 ↓
Qwen Code CLI
 ↓
Repository Acceptance Test

```

Optional after successful CLI validation:

- VS Code integration.

Do not initially install or build:

- Hermes,
- alternative Agent frameworks,
- MCP infrastructure,
- custom coding Agent,
- vector database,
- repository RAG,
- custom escalation router,
- multiple coding models,
- developer AI Gateway.

---

# 11. Qwen Code Acceptance Test

Before allowing Qwen Code to perform regular repository work:

1. Verify repository build/tests independently.
2. Ask Qwen to identify solution and project structure.
3. Give one bounded two-to-four-file task.
4. Require it to run the correct build/tests.
5. Review the Git diff.
6. Require explanation of behavioral changes.
7. Require unresolved problems to be stated explicitly.
8. Confirm requests are served by the local model.

Success is not:

```text
dotnet build = green

```

Success is:

> Human-accepted correct patch with less total effort than manual completion.

---

# 12. Developer AI Routing Policy

Task routing is based on:

**ambiguity + consequence**

rather than code size.

## Qwen Code Default

Use for:

- repetitive mappings,
- DTO changes,
- boilerplate,
- localized UI changes,
- documentation,
- established patterns,
- small refactors,
- clear bug fixes,
- bounded multi-file changes with good tests.

## Codex / Stronger Hosted Model

Prefer for:

- architecture,
- authorization,
- security,
- credentials/secrets,
- concurrency,
- database migration design,
- destructive operations,
- durable-job semantics,
- complex debugging,
- large uncertain refactors,
- ambiguous requirements.

Example:

```text
Task
  │
  ├── predictable / bounded
  │         ↓
  │      Qwen Code
  │
  └── ambiguous / high consequence
            ↓
          Codex

```

---

# 13. Qwen Escalation Rule

Stop using Qwen for the current task after:

- two failed correction attempts,
- approximately 15 minutes without meaningful progress,
- unexpected scope expansion,
- weakening/removing tests,
- validation removal,
- suspicious commands,
- unexplained changes,
- or whenever manual/Codex completion is clearly faster.

Escalation package should contain:

```text
Goal
Acceptance Criteria
Current Diff
Build/Test Results
Remaining Problem
Attempted Fixes

```

If Codex quota is unavailable:

> High-risk work waits.

Quota exhaustion must not lower engineering standards.

Use Qwen for another lower-risk task instead.

---

# 14. Developer AI Security

Qwen Code needs legitimate repository access, unlike a future general-purpose autonomous Agent.

Recommended boundary:

```text
Windows
   │
   ▼
Dedicated WSL2
   │
   ▼
Qwen Coding Sandbox
   │
   ├── Repository         RW
   ├── .NET SDK
   ├── build/test tools
   ├── approved cache
   └── local inference endpoint

```

Exclude:

```text
Windows personal drives
personal home directories
SSH credentials
browser credentials
production credentials
Docker daemon socket
unrestricted sudo
automatic push/deploy

```

Generated changes require human diff review before commit.

Qwen Code and Codex must not edit the same working tree simultaneously.

---

# 15. Operating Modes

Because the machine is shared between development, content generation, and gaming, initial resource management remains procedural.

## CODING MODE

```text
Pause new Content jobs
↓
Finish/cancel active GPU work
↓
Load coding model
↓
Run Qwen Code

```

## CONTENT MODE

```text
Stop Qwen task
↓
Unload coding model if different
↓
Load content model
↓
Resume Content workers

```

## GAMING MODE

```text
Stop new AI work
↓
Finish/cancel active work
↓
Unload models
↓
Stop local AI services
↓
Stop WSL where appropriate

```

No Resource Manager application is currently required.

Simple scripts are sufficient.

---
