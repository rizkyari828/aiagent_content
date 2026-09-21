# 13 Extractable Module Boundaries

> **Post-v0.9.2 addendum.** This file is *not* derived from the frozen v0.9.2 source and does **not** change it. The frozen source `docs/product/_source/Local_AI_Ecosystem_PRD_v0.9.2.md` and its SHA-256 in `PRD_INDEX.md` remain authoritative and unchanged. It records architecture guardrails adopted after the freeze, at the same level as the section 53 ADR candidates.

---

# A1. Modular first, distributed when operationally justified

AI Studio is a **.NET modular monolith today**. Co-location is the default deployment. A module may only move to another process or machine when a concrete operational reason exists, for example:

- it must run 24/7,
- it must run on a different machine or location,
- it has a distinct GPU vs CPU resource profile,
- it needs independent scaling,
- it needs security/trust isolation,
- it needs failure isolation,
- it has a substantially different lifecycle,
- or strong operational/business evidence requires it.

"Microservices are cleaner" is **not** sufficient justification.

# A2. Co-located by default, remotely extractable by contract

Modules stay co-located but are designed to be **remotely extractable by contract**. Cross-module boundaries go through application-owned ports/interfaces backed by immutable, serializable DTOs, so a later extraction replaces the transport adapter (local adapter → remote client) **without rewriting the business logic**. The remote transport itself is introduced only when extraction actually happens.

```text
TODAY:  Creative → IApprovedIdeaSource → LocalContentIdeaAdapter
FUTURE: Creative → IApprovedIdeaSource → RemoteContentIdeaClient   # business logic unchanged
```

# A3. Domain business models remain specific to their workflow

Conceptually similar workflows are **not** one domain model. Content and a future Stock domain may both contain "something interesting", but they are different models.

```text
Content: Trend Signal → Content Idea → Content Experiment → Publish
         → CTR / Retention / Revenue → Learning

Future Stock: Market Data → Screening Candidate → Thesis / Signal
              → Watchlist / Position Observation → Return / Drawdown / Hit Rate → Learning
```

`ContentIdea` ≠ `StockCandidate`; `ContentTrend` ≠ `MarketSignal`; `CTR` ≠ `InvestmentReturn`; `AudienceRetention` ≠ `Drawdown`. Do not force them into a shared generic model.

# A4. Shared capabilities emerge only from proven duplication

Prefer different domain modules plus **shared proven technical capabilities** over one universal business module with mode switches.

```text
GOOD:                                  BAD:
Content Intelligence → ContentIdea     IdeaService → Mode = Content
Stock Intelligence   → StockCandidate  IdeaService → Mode = Stock
        both may later reuse:
        ISourceFetcher, Scheduling, RetryPolicy,
        Provenance, Deduplication, AI model gateway
```

The gate is a **second real workflow**: implement the domain naturally, observe real duplication, identify the identical technical capability, extract the smallest reusable capability, and preserve the domain-specific business models. Until the second workflow exists, do not abstract.

# A5. Illustrative future Content Intelligence / Stock Intelligence topology

This is an **example only, not committed implementation**. The default is one modular application. A later topology *might* look like:

```text
VPS 24/7:
  Content Intelligence      (trend collector, research, idea candidates)
  Future Stock Intelligence (market collector, screening, market research)
  Analytics workloads       (content metrics, future stock outcome analytics)

Home GPU node:
  Creative / Story, Qwen, FLUX, Blender, VoxCPM, ACE-Step, FFmpeg
```

Content Intelligence and Stock Intelligence do **not** need to be separate physical services immediately. They may begin as one host with two modules and split only on operational evidence.

# A6. Distributed microservices remain deferred

No message broker (RabbitMQ/Kafka/NATS), service discovery, API gateway, Kubernetes, per-module database, distributed transactions, or microservice networking until operational evidence justifies a specific extraction. PostgreSQL and in-process calls remain the initial coordination mechanisms.

# A7. Current Content Studio must not become more complicated for a hypothetical future

Do not create stock entities, crawlers, services, generic intelligence/scoring frameworks, or `UniversalIdea`/`UniversalAnalytics` models merely because a future Stock Intelligence domain may exist. Business abstraction follows demonstrated variation, not imagined reuse.

---

# ADR-018 Extractable Module Boundaries

**Status:** Accepted (post-v0.9.2).

- **Co-location default:** one modular application with minimal processes.
- **Extraction seam:** cross-module interaction goes through application-owned ports and immutable, serializable contracts; extraction replaces the transport adapter and leaves business logic substantially unchanged.
- **No cross-module persistence access:** a module never uses another module's `DbContext`, `DbSet`, EF entity, repository implementation, or table as its integration API. A shared PostgreSQL deployment is fine; logical data ownership stays explicit.
- **No cross-module Infrastructure dependency:** a module never depends on another module's Infrastructure implementation; consumers depend on an application-owned port and a local adapter.
- **Transport-neutral contracts:** cross-module contracts/events are pure serializable data, never EF entities, provider instances, delegates, commands, or runtime implementation types.
- **Operational justification first:** a module is extracted only for a concrete operational reason, and remote transport is introduced only when the extraction happens.

# ADR-019 Domain-Specific Workflows and Proven Capability Reuse

**Status:** Accepted (post-v0.9.2).

- **Domain workflows stay specific:** Content and a future Stock workflow are separate domain models; do not create universal Idea/Analytics/`Generic<T>` domain abstractions for hypothetical reuse.
- **Reuse requires evidence:** a shared capability is extracted only after a **second real workflow** demonstrates duplication.
- **Smallest capability:** extract the smallest proven technical capability (scheduling, ingestion, retry/rate limiting, provenance, deduplication, AI model access, common job infrastructure), not a universal business module.
- **Preserve domain models:** domain-specific business meaning stays inside its own module; shared capabilities must not absorb it.
