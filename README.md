# Local AI monorepo

This repository contains the Content Studio product and the reusable tooling used to configure, verify, measure, and improve local AI workloads.

## Solutions

- [`ai-studio/`](ai-studio/) — Content Studio application, durable jobs, API, web client, tests, and product documentation.
- [`local-ai-infra/`](local-ai-infra/) — reusable local AI profiles, model and hardware metadata, configuration tooling, telemetry schemas, evals, and learning-data conventions.

The boundary is deliberate: Content Studio declares **what** model and behavior the product needs, while Local AI Infra describes **how** the local runtime is configured and evaluated. `local-ai-infra/` is not a required runtime dependency of Content Studio.

Use each solution from its own directory:

```bash
cd ai-studio
# See ai-studio/README.md
```

```bash
cd local-ai-infra
# See local-ai-infra/README.md
```

Repository-wide agent routing and safety rules live in [`AGENTS.md`](AGENTS.md). Detailed commands and architecture remain in each solution's documentation.
