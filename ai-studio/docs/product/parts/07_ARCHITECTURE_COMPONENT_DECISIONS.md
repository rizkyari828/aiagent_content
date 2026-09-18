# 07 Architecture Component Decisions

> Derived split of **Local AI Ecosystem PRD v0.9.2**. The numbered sections below are preserved from the frozen source PRD; this file does not introduce new product decisions.

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
