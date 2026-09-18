# Local AI Ecosystem PRD v0.9.2

**Status:** Architecture Freeze
**Strategy:** Build → Publish → Measure → Learn → Automate → Scale → Profit
**Primary Operator:** One developer/operator
**Primary Validation Product:** Local AI Content Studio
**Primary Validation Channel:** Operator-owned YouTube channel
**Hardware:** Single Windows PC, NVIDIA RTX 4060 Ti 16 GB VRAM
**Pre-validation Added Budget Target:** Rp0

**Revision Focus:** Agent restraint, application-owned knowledge, evidence-based reusability
**Previous Baseline:** v0.9.1

## v0.9.2 Change Summary

This revision incorporates the latest architecture review without changing the core Content Studio direction.

Key changes:

- Content Studio remains the sole workflow/business authority.
- Hermes is clarified as an optional bounded executor, never the studio manager or source of truth.
- Named responsibilities do not imply persistent Agents.
- Application-owned knowledge replaces planned specialist memory as the default learning mechanism.
- Human correction reasons become first-class learning evidence.
- Lead/Strategy/multi-Agent organization remains deferred until a measured bottleneck proves value.
- Telegram is reduced to an optional secondary operations surface, starting with at most one bot if needed.
- Reusable AI Workforce concepts may only be extracted after a second real workflow exposes repeated duplication.
- Multi-Agent adoption must beat simpler baselines on operator time, error reduction, and resource cost.

---

# 1. Executive Summary

Local AI Ecosystem is a local-first initiative intended to create useful AI-powered products and eventually sustainable profit.

The first product is:

**Local AI Content Studio**

Its immediate purpose is not to prove that a sophisticated AI platform can be built.

Its purpose is to prove that one operator can:

1. produce useful content,
2. publish repeatedly,
3. learn from audience behavior,
4. reduce production effort,
5. test monetization,
6. and generate real revenue.

The first business hypothesis is:

> Can one operator produce a specific kind of useful content for a specific audience at a sustainable human cost, and convert part of that audience into revenue?

The first architecture therefore favors:

- deterministic workflows,
- simple local AI integration,
- durable PostgreSQL Jobs,
- explicit artifacts,
- human approval,
- local tools,
- and real-world validation.

Advanced Agent infrastructure remains a future option.

It is not a destination the project is obligated to reach.

The agent-enabled baseline, if a measured need appears, is:

```text
Content Studio
+
PostgreSQL Jobs
+
deterministic workflows
+
direct model calls
+
at most one bounded optional reasoning assistant
```

Additional agents must earn their place through measurable reduction in operator effort, material error rate, or production bottlenecks.

---

# 2. Core Principle

The project follows:

```text
Build
  ↓
Publish
  ↓
Measure
  ↓
Learn
  ↓
Automate
  ↓
Scale
  ↓
Profit

```

Automation is introduced only after a production bottleneck is observed.

Architecture must follow evidence.

Not the reverse.

---

# 3. Success Hierarchy

## Technical Success

A useful video can move reliably from idea to publishable artifact.

## Operational Success

The workflow can repeat with decreasing human effort and acceptable quality.

## Product Success

Real audiences consume, engage with, return to, or recommend the content.

## Business Success

Audience demand can be converted into sustainable economic value.

Architecture sophistication is not itself a success metric.

---

# 4. Product Scope

## NOW

### Local AI Content Studio

The only production product required before validation.

Responsibilities:

- content projects,
- idea generation,
- research support,
- script generation,
- storyboards,
- scene planning,
- assets,
- narration,
- subtitles,
- rendering,
- publishing workflow,
- analytics,
- revenue experiments.

---

## LATER

Potential future products:

- AI Chat,
- Agent-based research assistant,
- internal AI tools,
- reusable AI capabilities extracted from proven duplication,
- external AI products,
- a second bounded operational workflow such as Opportunity Hunter.

A reusable AI Workforce Layer is not a committed product or target architecture.

It may only emerge after at least two real workflows demonstrate repeated shared needs.

Their future existence must not increase the complexity of Video #1.

---

# 5. Architecture Philosophy

Do not build an abstraction because it might theoretically be reusable.

Build an abstraction when:

1. there is a current caller,
2. the boundary improves maintainability or safety,
3. multiple implementations genuinely exist or are expected soon,
4. or the abstraction significantly reduces coupling.

Therefore, v0.9.2 intentionally removes several speculative Platform and multi-agent abstractions from the immediate implementation roadmap.

Additional rules:

```text
Clean boundaries != generic framework

Role != Agent

Responsibility != Process

Similar names != reusable abstraction
```

A Research responsibility may initially be a normal Job, a prompt, or a human-assisted step.

Do not create a persistent Research Agent merely because the responsibility exists.

Shared capabilities are extracted only after real duplicated behavior exists across at least two operational workflows.

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

# 26. Content Production Pipeline

Target workflow:

```text
Topic
  ↓
Idea
  ↓
Research
  ↓
Script
  ↓
Storyboard
  ↓
Scene Manifest
  ↓
Assets
  ↓
Narration
  ↓
Subtitle
  ↓
FFmpeg
  ↓
QA
  ↓
Final Video
  ↓
Human Approval
  ↓
Publish

```

Not every stage requires automation.

---

# 27. Video #1 Minimal Workflow

Video #1 must intentionally contain manual steps.

Recommended:

```text
Topic / Problem selection       Human
Idea support                    AI
Script draft                    AI
Script editing                  Human
Research/fact checking          Human + AI support
Storyboard                      Simple structured draft
Visual collection               Mostly manual
Narration                       Manual or simple local TTS
Composition                     FFmpeg
QA                              Human
Publishing                      Human
Analytics recording             Manual

```

The goal is to validate content.

Not automation coverage.

---

# 28. Video Generation

Heavy generative video is deferred.

Progression:

```text
Manual / sourced visual
        ↓
Generated image if useful
        ↓
Simple motion
        ↓
Image-to-video selectively
        ↓
Generated video only after evidence

```

FFmpeg remains the primary deterministic composition engine.

Remotion and Blender remain deferred unless production evidence requires them.

---

# 29. Audience Definition Before Video #1

Before selecting Video #1, define:

```text
Audience
+
Recurring Problem
+
Content Format
+
Offer Hypothesis

```

Example structure:

```text
Audience:
Who specifically benefits?

Problem:
What recurring problem costs them time, money, or frustration?

Content:
What free useful output demonstrates value?

Offer:
What optional paid outcome saves them additional effort?

```

The initial niche does not need to be permanently correct.

It needs to be specific enough to generate learnable evidence.

---

# 30. First Revenue Experiment

Do not wait for AdSense as the only monetization test.

Preferred structure:

```text
Useful Free Content
        ↓
Related Small Offer

```

Possible experiments:

- focused digital asset,
- template,
- checklist,
- sample implementation,
- educational pack,
- bounded walkthrough,
- tightly scoped service,
- affiliate revenue where genuinely relevant.

Do not build:

- checkout infrastructure,
- subscriptions,
- customer portal,
- automated fulfillment,

before a real buyer exists.

Manual fulfillment is acceptable.

---

# 31. Economic Measurement

Before revenue track:

```text
Human Hours / Video
Elapsed Production Time
Repair/Rework Time
Direct Cash Cost
Views
Audience Responses
Offer Visits / Inquiries

```

After first revenue:

```text
Collected Revenue
Refunds / Fees
Fulfillment Time
Support Time
Variable Cost

```

Use:

```text
Cash Contribution
=
Collected Revenue
- Refunds
- Fees
- Incremental Cash Costs

```

and:

```text
Economic Contribution
=
Cash Contribution
- Human Hours × Chosen Hourly Value

```

Engineering time must also be tracked.

Rp0 additional software purchase does not mean production cost is zero.

---

# 32. Content Learning

Early analysis remains simple:

```text
SQL
+
simple statistics
+
manual interpretation
+
AI-assisted analysis

```

Useful metrics may include:

- impressions,
- views,
- CTR,
- average view duration,
- retention,
- subscribers,
- comments,
- offer visits,
- inquiries,
- revenue.

Avoid ML infrastructure before sufficient data exists.

## Human Correction Capture

For every meaningful human revision, capture a small reason when practical.

Examples:

```text
unsupported_claim
hook_too_slow
too_verbose
tone_too_formal
weak_source
visual_too_expensive
asset_rights_unclear
story_flow_weak
```

Do not require elaborate annotation.

The purpose is to learn what repeatedly creates operator work.

Useful metrics include:

```text
Corrections / Video
Correction Minutes / Video
Most Frequent Correction Reasons
Accepted First-Draft Rate
Rework by Pipeline Stage
```

This correction history is more valuable initially than persistent Agent personality or private memory.

## Application-Owned Knowledge

Knowledge is organized by subject and scope, not by Agent identity.

Examples:

```text
ChannelGuideline
AudienceInsight
ContentInsight
ProductionLesson
ExperimentObservation
BrandGuideline
```

Lifecycle:

```text
Observation
    ↓
Candidate Lesson
    ↓
Reviewed Scoped Hypothesis
    ↓
Supported Operating Guideline
    ↓
Re-evaluate / Supersede / Retire
```

For meaningful lessons retain:

- scope,
- supporting evidence,
- contradictory evidence,
- observation dates,
- measurement window,
- relevant denominator/context,
- revision history,
- current status.

Agents may propose knowledge.

They must not silently promote their own observation into trusted operating guidance.

Private runtime memory remains optional convenience and is never the business source of truth.

---

# 33. Review Gates

## Videos 1–3

Goal:

- prove pipeline viability.

Measure:

- human hours,
- failures,
- rework,
- basic audience behavior.

If every video requires repeated infrastructure repair:

> simplify.

---

## Videos 4–10

Goal:

- establish one repeatable format,
- test topic/packaging hypotheses,
- test a real monetization offer.

Track production-time trend.

Automation work must be justified by observed bottlenecks.

---

## Videos 10–30

Evaluate:

- audience trend,
- qualified traffic,
- purchase/inquiry evidence,
- production efficiency,
- support burden,
- economic contribution.

Thirty videos are not an obligation.

Weak evidence after deliberate experiments should trigger pivot/simplification rather than sunk-cost engineering.

## Agent Adoption Gate

An Agent is not adopted because multi-agent architecture is interesting.

Add a bounded Agent only when:

1. a recurring bottleneck is measured,
2. adaptive reasoning/tool selection is genuinely useful,
3. a simpler prompt, Job, deterministic rule, or UI improvement is insufficient,
4. total operator time is reduced after including review/recovery,
5. resource use remains acceptable on the single GPU,
6. failures are bounded and recoverable.

Before adding another specialist, compare:

```text
A. Deterministic workflow + direct model call

B. One bounded Agent + tools

C. B + one selective reviewer/specialist
```

Use representative tasks and the same acceptance rubric.

A practical screening target is approximately 20% or greater repeatable reduction in total operator time, or a meaningful reduction in consequential errors that clearly justifies the added operational cost.

This threshold is a project decision rule, not a claim of statistical significance.

---

# 34. Architecture Components — BUILD NOW

```text
ContentProject
GenerateIdea
GenerateScript
PostgreSQL Jobs
Task-specific typed inputs/results
IAiTextGenerator
Prompt templates
Basic context construction
Artifact metadata
Cancellation/attempt safety
Backup and restore
FFmpeg rendering
Basic analytics/revenue recording
Phase 0 Local Developer AI Bootstrap

```

---

# 35. Architecture Components — KEEP VERY SIMPLE

```text
React UI
Provider configuration
Evaluation fixtures
Structured logging
Resource coordination
Transcription adapter where needed
Storyboard generation
Scene Manifest
Artifact lineage

```

---

# 36. Architecture Components — DEFER

```text
Hermes
IAgentRuntime
Agent Gateway
Agent Registry
Hermes Skills
Agent Memory
LeadAgent
Subagents
Tool Gateway
MCP
RAG
Embeddings
pgvector
Capability Router
Resource Manager
Automated analytics ingestion
Automated publishing
AI Chat
Multi-tenancy
Billing
Advanced generative video
Multiple Telegram Agent personalities
Team Template UI / Team DSL
Portfolio Lead
Generic Agent identity framework
Multi-layer private/shared/global Agent memory
Automatic knowledge promotion
Experiment Agent
LLM Resource Manager
Always-on autonomous planning loops
Recursive delegation

```

---

# 37. Architecture Components — REMOVE FROM CURRENT PLAN

```text
Full Custom Agent Platform
Universal Agent Protocol
Generic TaskContract Framework
Custom Skill Runtime
Custom Semantic Memory Engine
Custom Vector Database
Custom Workflow Engine
Custom Coding Agent
Model Registry Service
Agent-per-Microservice architecture
Kubernetes-style local scheduling

```

These ideas may be reconsidered only if future evidence creates a real need.

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

# 46. Revised Execution Roadmap

## PHASE 0 — Local Developer AI Bootstrap

Maximum active effort:

**3 hours**

Tasks:

```text
Validate WSL2 environment
↓
Validate existing inference runtime
↓
Use existing Qwen first
↓
Install Qwen Code CLI
↓
Configure local endpoint
↓
Establish coding sandbox
↓
Run repository acceptance task
↓
Document usage + escalation

```

Exit:

```text
PASS → use Qwen for appropriate development tasks
FAIL → stop setup and continue without it

```

---

# 47. NOW — Video #1

Only work needed for the next product milestone:

```text
Define Audience
      ↓
Define Problem
      ↓
Define Format
      ↓
Define Offer Hypothesis
      ↓
Validate GenerateIdea
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
🔥 VIDEO #1
      ↓
Publish Manually
      ↓
Record Metrics + Human Time

```

No Hermes dependency.

---

# 48. NEXT — Videos 2–10

Observe friction first.

Then fix the largest bottleneck.

Potential improvements:

- reusable rendering template,
- resumability,
- better Artifact metadata,
- improved prompt fixtures,
- lightweight research automation,
- analytics capture,
- optional TTS,
- selective image generation,
- human correction-reason capture,
- simple reviewed knowledge notes,
- one status/notification channel only if remote visibility saves meaningful time.

If research is demonstrably expensive:

> run bounded Hermes experiment.

If research is not a problem:

> do not install Hermes.

---

# 49. LATER

After real validation:

```text
Bounded Hermes / Agent Runtime trial
Selective Reviewer
Read-only autonomous research
Second operational workflow
Shared capability extraction after proven duplication
RAG / pgvector only after a retrieval problem exists
advanced Channel Brain only after enough validated knowledge exists
selective generated video
publishing integration
AI Chat
public infrastructure
quotas
billing
multi-tenancy
```

Each must receive its own economic justification.

Do not assume that successful use of Qwen Code as developer tooling validates production Agents.

---

# 49A. Reusable Capability Extraction Gate

Reusable infrastructure is extracted from real duplication, not from anticipated future teams.

Extract a shared capability only when all conditions hold:

1. at least two distinct operational workflows are used for real decisions or outputs,
2. both repeatedly need substantially the same behavior,
3. duplicated maintenance or fixes actually exist,
4. a common contract is clear without many domain exceptions,
5. extraction has credible near-term maintenance payoff,
6. both workflows remain independently testable after extraction.

Do not preemptively add:

```text
TeamId
AgentRegistry
AgentIdentity
Team DSL
Generic Memory Interface
Universal Task Protocol
```

merely because a future workforce concept could use them.

A clean implementation today is enough.

---

# 49B. Second Use Case — Opportunity Hunter

A second workflow may be introduced only after Content Studio has produced real operational evidence and the additional workflow does not block publishing.

Its first version is intentionally bounded.

Example decision:

> For one audience and one problem, identify up to three plausible opportunities, show supporting and contrary evidence, and recommend the cheapest next demand test.

Initial implementation:

```text
Human Goal
   ↓
Bounded Research / Analysis Job
   ↓
Structured Evidence Brief
   ↓
Human Decision
```

Do not initially create:

```text
Opportunity Lead
Market Agent
Finance Agent
Technical Agent
Critic hierarchy
Automatic product-building loop
```

The purpose of the second use case is to reveal genuine reusable behavior.

It is not to prove that a multi-Agent company can be simulated.

---

# 49C. Optional Remote Operations / Telegram

Telegram is secondary to the Content Studio UI.

Do not create multiple Agent bots initially.

If remote visibility becomes useful, begin with at most one bot for deterministic commands such as:

```text
/status
/pause
/cancel
/attention
```

Operational facts must come from authoritative application telemetry.

If no reliable percentage exists, report:

```text
Rendering
```

rather than inventing progress such as `63%`.

Telegram is not a durable business queue.

Accepted commands must be persisted and deduplicated by Content Studio.

Consequential approvals remain in the Content Studio UI initially.

---

# 49D. Reviewer Policy

Use independent review selectively.

```text
Deterministic validation
→ schema, required fields, paths, assets, duration, encoding

Evidence review
→ unsupported claims, conflicting sources, attribution

Human review
→ usefulness, audience fit, misleading presentation, final publication
```

A second persona using the same model is not an independent factual authority.

Start with at most:

```text
1 review pass
+
1 revision pass
```

Do not build reviewer-of-reviewer chains.

---

# 49E. Future Agent Shape

If an Agent becomes justified, start with one bounded production profile or runtime configuration.

The useful first Agent is closer to:

```text
Research / Planning Assistant
```

than:

```text
Executive AI Manager
```

Do not create separate persistent Lead, Strategy, Creative, Growth, Analytics, and Reviewer profiles before distinct retained contexts/configurations demonstrably require them.

Persistent Agent configuration does not imply:

- continuous execution,
- authority over workflow,
- private business memory,
- or a dedicated Telegram identity.

Temporary delegated workers may be considered later for isolated bounded tasks, but delegation must share the parent budget and must not increase authority.

---

# 50. DO NOT BUILD YET

Explicitly excluded:

```text
Full Agent Platform
LeadAgent
Multi-agent hierarchy
Dynamic Agent Registry
Skill management UI
Persistent semantic Agent memory
Agent self-improvement pipeline
General Tool Gateway
Model Registry service
Capability Router
Complex Resource Manager
Vector database
Custom workflow engine
Custom coding agent
Kubernetes
Distributed microservices
AI Chat product
SaaS billing
Full generative-video pipeline
Autonomous publishing
AI Workforce platform
Executive / Strategy / Creative / Growth Agent hierarchy
Persistent profile per specialist
Portfolio Lead
Team-template UI / Team-definition DSL
Universal task protocol
Generic Agent identity / registry framework
Automatic knowledge promotion
Embeddings/vector infrastructure without a measured retrieval problem
Recursive Agent delegation
Always-on autonomous planning loops
LLM Resource Manager
Dynamic model auctions / elaborate capability routing
Multiple Telegram personalities
Parallel Hermes and PostgreSQL scheduling authorities

```

---

# 51. Future Local AI Ecosystem Vision

The long-term vision remains broader than Content Studio, but the architecture is allowed to emerge rather than being pre-declared.

Possible future evolution:

```text
                 LOCAL AI ECOSYSTEM
                        │
          ┌─────────────┼─────────────┐
          ▼             ▼             ▼
   Content Studio   Second Real    Future Real
                    Workflow       Products
          │             │             │
          └─────────────┼─────────────┘
                        │
              Observe Real Duplication
                        │
                        ▼
             Extract Shared Capabilities
                        │
              ┌─────────┼─────────┐
              ▼         ▼         ▼
           Models    Evidence   Bounded
           / Tools    Handling   Agent Runtime*
```

`*` Only when economically justified.

A reusable AI Workforce Layer may eventually emerge from these shared capabilities.

It is not a required destination, not an Architecture Freeze dependency, and not a reason to pre-build Agent organization concepts.

The extraction sequence is:

```text
First real workflow
        ↓
Second real workflow
        ↓
Repeated duplicated behavior
        ↓
Shared capability
        ↓
Only then consider a broader workforce abstraction
```

This is:

**Evolutionary Direction**

not:

**current target architecture.**

---

# 52. Provider Independence

Provider independence remains a design principle, not a mandate to pre-build interfaces.

The system should avoid unnecessary direct coupling where practical.

But:

> Abstraction follows demonstrated variation.

Therefore:

```text
IAiTextGenerator

```

exists because it is currently useful.

Future interfaces such as:

```text
IAgentRuntime
IImageProvider
IVideoProvider

```

should only be introduced when actual callers exist.

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