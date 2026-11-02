# I2V Home Task — Execution Prompt (GPU machine)

Use this file to run the home-side steps in a **separate session**, without re-briefing.

- Target machine: home PC (RTX 4060 Ti 16 GB, WSL2, ComfyUI 0.36.0, Blender 5.2.2 LTS,
  Ollama `qwen3.8:27b-q4_K_M`, VoxCPM2, ACE-Step per existing docs — verify, do not assume).
- Model routing (local-ai-infra `ai_infra_route`, 2026-11-02): **BALANCED / high → `deepseek-flash`**
  (`RECOMMEND_ONLY`). Start the session with `deepseek-flash` and effort `high`. Escalate only if
  the benchmark surfaces deep CUDA/driver debugging.
- Entry message: paste everything between `BEGIN PROMPT` and `END PROMPT`.

```text
BEGIN PROMPT

# ROLE
You are executing the home-GPU side of the I2V milestone: sync the unpushed Mac work, author the
real ComfyUI video workflow, and benchmark two engines. You are NOT redesigning the C# contracts;
they already exist and are frozen by an ADR.

# READ FIRST (in order)
1. AGENTS.md (repo root) and ai-studio/AGENTS.md
2. ai-studio/docs/agent/CHECKPOINT.md
3. ai-studio/docs/context/STATE.md ("Known issues", Verification entry format)
4. ai-studio/docs/development/I2V_PLAN.md   <-- your checklist: P0.1, P1.6, P2.1-P2.3
5. ai-studio/docs/development/DECISIONS.md  (D-046 = I2V boundary you must not break)
6. ai-studio/src/AIStudio.Infrastructure/Rendering/ComfyUiVideoGenerationProvider.cs,
   ComfyUiVideoOptions.cs, src/AIStudio.Application/Rendering/Visuals/IVideoGenerationProvider.cs
7. ai-studio/docs/development/I2V_AGENT_TASK.md (what was already built on the Mac)

# P0.1 — SYNC FIRST (blocker)
- `git status` and `git log --oneline -5`. If the 2026-10-01 Ken Burns v2 + anime Video #1 work is
  still local/uncommitted here, commit it on its own branch first, run the suite, then merge to
  `main` and push. Never mix it with I2V commits.
- The Mac scaffold lives on `feat/i2v-scaffold` (4 commits) and the docs on
  `docs/video1-state-cleanup`. If `git ls-remote --heads origin` does not show them, they were not
  pushed yet: ask the operator to push from the Mac, then `git fetch --all --prune`.
- Rebase/merge `feat/i2v-scaffold` onto the updated `main`; resolve conflicts by keeping BOTH
  changes. Expect overlap only in `DependencyInjection.cs`, `CapabilityIds.cs`, and test files.
- Verify: `dotnet build AIStudio.slnx -c Release` 0 warnings/0 errors; full suite green with
  `ConnectionStrings__DefaultConnection` set; record the exact numbers in STATE.

# P1.6 — AUTHOR THE REAL WORKFLOW (no invented nodes)
- Record the runtime facts first: ComfyUI version, installed model filenames, and the node list
  from `GET /object_info` (read-only). Save that evidence under `.demo/artifacts/i2v-bench-<UTC>/`.
- Author two repository-owned graphs in ComfyUI API format:
  - `src/AIStudio.Infrastructure/Rendering/ComfyUI/i2v_workflow.json` (text-only)
  - `src/AIStudio.Infrastructure/Rendering/ComfyUI/i2v_workflow_first_frame.json` (one first frame)
- Use ONLY the placeholders the provider substitutes, exactly as the provider expects them:
  `__PROMPT__`, `__WIDTH__`, `__HEIGHT__`, `__FPS__`, `__DURATION_SECONDS__`, `__SEED__`,
  `__REFERENCE_IMAGE__`. Read the provider code before editing the JSON; do not invent new
  placeholders or rename these.
- Every `class_type` must exist in the installed `/object_info`. Every model/checkpoint filename
  must be one actually installed. If something is missing, report it instead of guessing.
- Keep the graph trusted and repository-owned: no caller-supplied graph, no dynamic nodes, no
  shell, no unvalidated model path.

# P2.1 — VERIFY THE RUNTIME
- ComfyUI reachable at `ComfyUiVideo:BaseUrl`; `ComfyUiVideo:Enabled=true` only for the smoke run;
  `WorkflowPath`/`FirstFrameWorkflowPath` pointing at the files you authored; bounds
  (`Width`/`Height`/`Fps`/`MaxSeconds`/`TimeoutSeconds`) sane for 16 GB VRAM.
- Prove it with a single-scene smoke run through `ComfyUiVideoGenerationProvider` (not the full
  pipeline). A fake-HTTP test does not prove the graph.

# P2.2 — BENCHMARK (one scene, both engines)
- Scene: a shadow figure raising a hand while golden light grows around it. 3–5 s, fixed seed,
  same prompt and first frame for both engines.
- Engines: LTX-Video 0.9.x 2B (primary candidate) vs Wan2.2 TI2V-5B (quality alternative).
- Record for each run: peak VRAM, wall time, output resolution/fps/duration, file size + SHA-256,
  face/hand morph, prompt adherence, temporal stability. Store artifacts under
  `.demo/artifacts/i2v-bench-<UTC>/` (one JSON + one MP4 per run).
- Never run a heavy FFmpeg job while ComfyUI is generating; serialize GPU work.

# P2.3 — GO/NO-GO
- Pick the primary engine and record why in `DECISIONS.md` (new ADR) and `STATE.md`.
- If both fail the quality bar, say so explicitly and record NO-GO with the evidence.

# OPTIONAL (only if the benchmark passes)
- P3.1: replace the MP4/WebM signature check in the provider with `IMediaInspector` verification.
  Keep the GPU lease across submit -> poll -> download and release it before CPU persist.

# HARD RULES
- Do NOT rename `visual.character_animation`, the `video_*` codes, or the request/result shape.
  Any contract change needs an ADR and a Mac-side follow-up.
- Do NOT invent nodes, models, or workflow files that you cannot verify against `/object_info`.
- Do NOT add NuGet packages, migrations, endpoints, or capability ids.
- Do NOT force push. Keep I2V commits separate from the Ken Burns sync commits.
- Never commit artifacts, models, logs, `ai-studio/.env`, or generated MP4s.

# EVIDENCE AND DOCS (same branch as the work)
- `DECISIONS.md`: ADR with the engine choice + workflow contract facts (ComfyUI version, node set).
- `STATE.md`: one entry in the existing format,
  `PASS (home real, <date>) - I2V benchmark v1: <commands> -> <exact numbers>`.
- `CHECKPOINT.md`: update the resume line.
- `I2V_PLAN.md`: tick P0.1, P1.6, P2.1-P2.3 only when verified.
- `REPO_MAP.md`: list the two workflow JSON files.

# REPORT FORMAT
1. Summary (5-10 lines): synced, authored, benchmarked, decided.
2. Branch + commits (hash + message), what was merged to main.
3. Files created/changed.
4. Evidence table: ComfyUI version, nodes used, VRAM peak, wall time, output specs, hashes.
5. GO/NO-GO decision + reasoning.
6. Blockers and what the Mac side must do next (P3 routing/E2E handoff).

# STOP AND ASK
If a required model/node is missing, if the workflow needs a node that is not installed, if VRAM
is insufficient, or if the two engines disagree in a way that needs a product decision.

END PROMPT
```
