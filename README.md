# Content Studio repository

This repository contains the Content Studio product and the repository-wide agent routing and safety rules.

Shared local AI infrastructure now lives in a **separate sibling repository**:

- `../local-ai-infra/` — reusable local AI profiles, model and hardware metadata, configuration tooling, telemetry schemas, evals, and learning-data conventions. It is not part of this repository; see its own `README.md` and `AGENTS.md`.

The boundary is deliberate: Content Studio declares **what** model and behavior the product needs, while Local AI Infra describes **how** the local runtime is configured and evaluated. Local AI Infra is not a required runtime dependency of Content Studio; when a task needs it, locate it through the `LAI_REPO` environment variable (default `../local-ai-infra`).

## Solutions

- [`ai-studio/`](ai-studio/) — Content Studio application, durable jobs, API, web client, tests, and product documentation.
- `local-ai-infra/` (external sibling repository) — reusable local AI profiles, model/hardware metadata, configuration tooling, telemetry schemas, evals, and learning-data conventions.

Use each repository from its own directory:

```bash
cd ai-studio
# See ai-studio/README.md
```

```bash
cd ../local-ai-infra
# See that repository's README.md
```

Repository-wide agent routing and safety rules live in [`AGENTS.md`](AGENTS.md). Detailed commands and architecture remain in each repository's documentation.
