# 09 Publishing Approvals And Backups

> Derived split of **Local AI Ecosystem PRD v0.9.2**. The numbered sections below are preserved from the frozen source PRD; this file does not introduce new product decisions.

---

# 43. Publishing

Initial publishing remains manual.

Workflow:

```text
Final Video
    ↓
Human QA
    ↓
Explicit Approval
    ↓
Manual Publish

```

Automatic publishing may be introduced only after:

- content workflow is reliable,
- credentials are isolated,
- idempotency is clear,
- explicit approval policy exists.

An approval must bind to the actual artifact version, destination, and relevant metadata.

If the approved artifact or consequential metadata changes, approval becomes invalid and must be requested again.

---

# 44. Approval Levels

Autonomy is granted per action under explicit conditions, not as a global trust level awarded to an Agent.

A task may remain human-assisted forever if that produces the best economic outcome.

## A1 — Automatic

Examples:

- idea draft,
- script draft,
- analysis,
- structured generation.

## A2 — Human Review

Examples:

- final script,
- storyboard,
- thumbnail,
- final video.

## A3 — Explicit Approval

Examples:

- publishing,
- destructive deletion,
- credential changes,
- infrastructure permission expansion,
- trusted automation changes.

---

# 45. Backups

Back up:

- PostgreSQL,
- important source assets,
- irreplaceable artifacts,
- configuration,
- prompt templates,
- project metadata.

A second copy on the same physical drive protects against some accidental changes but not disk failure.

Backup strategy may remain simple before revenue, but recovery must be possible.

---
