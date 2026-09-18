# Video #1 Execution Context — Assets

**Derived from v0.9.2 sections:** 20, 26, 27, 47.  
**Purpose:** bounded context for asset collection/creation. No new requirements are introduced here.

## What v0.9.2 requires

- Assets are an explicit stage between Scene Manifest and Narration in the target pipeline.
- Video #1 visual collection should be **mostly manual**.
- The NOW roadmap says **Collect / Create Assets** after Basic Storyboard.
- For externally sourced assets, artifact metadata should record relevant provenance/licensing information.
- File existence alone is not sufficient proof that an artifact is valid.

## What v0.9.2 does not specify

The PRD does not freeze a stock provider, image-generation model, asset search API, download strategy, or dedicated asset database schema.

Heavy generative video is explicitly deferred; Video #1 does not depend on it.

## Minimal reading path

1. This file.
2. `../parts/04_JOBS_AND_ARTIFACTS.md` for artifact lifecycle/provenance.
3. `../parts/05_VIDEO1_PRODUCTION_PIPELINE.md` for production ordering.
