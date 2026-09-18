# 06 Audience Revenue Learning And Review

> Derived split of **Local AI Ecosystem PRD v0.9.2**. The numbered sections below are preserved from the frozen source PRD; this file does not introduce new product decisions.

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
