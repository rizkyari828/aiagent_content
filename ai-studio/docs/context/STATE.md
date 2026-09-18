# Work state

Updated: 2026-09-18.

## Current milestone

Video #1 is DONE. One real ContentProject executed the existing vertical slice end to end and produced a playable MP4.

## Implemented

- Video #1 E2E run: ContentProject `01b7f0ef-e749-4e1d-bbad-a93d016b2be2`; GenerateIdea -> GenerateScript -> approved ReviewedScript -> GenerateStoryboard (8 scenes) -> 8 scene assets -> narration -> subtitle -> RenderVideo -> FinalVideoQa.
- Project AI stayed local (Ollama `qwen3.8:27b-q4_K_M`); no paid or external provider was used.
- Config correction: `Ollama:TimeoutSeconds` raised 120 -> 600. The validated model generates at roughly 9 tokens/second, so the GenerateScript 3000-token budget exceeded the old 120s client timeout and failed with `ai_timeout` across all retries; the existing `1..600` validation bound already anticipated this.
- Routine reasoning off: the typed `AiTextRequest` now carries an optional `Think` flag that maps to the Ollama `/api/chat` `think` field. `GenerateScriptJobHandler` requests `Think: false`; callers that leave it null keep the previous thinking behavior. A fresh GenerateScript job (`012aaac4-fdcf-4b2f-b3a6-82f4d42e7c60`) completed with `retryCount` 0 in ~76s / 648 output tokens; the earlier failed job (`196926e0-f9a1-48f7-a360-ac52ec320945`) is preserved untouched.
- Artifact: `src/AIStudio.Api/assets/renders/01b7f0efe7494e1dbbada93d016b2be2/1526be4b6d434f54999782cc368a1990.mp4` (499638 bytes, 24s, 1280x720, h264/aac/mov_text, SHA-256 `2a1e6980da36edadbbed375bbde8fd802669892b1ca2ecfcb726ef74b7884fb8`).
- Untracked local media under `src/AIStudio.Api/assets/` was intentionally preserved for manual inspection and is not committed.

## Verification

- PASS - 251/251 tests (0 failed, 0 skipped) via `dotnet test tests/AIStudio.Tests/AIStudio.Tests.csproj` with `ConnectionStrings__DefaultConnection` set; the two added assertions cover `think` serialization and the GenerateScript handler's `Think: false`.
- PASS - `/health/ready` returns HTTP 200 (PostgreSQL Healthy) and the persistent job worker is active.
- PASS - final MP4 re-probed with `ffprobe`; SHA-256 matched the persisted RenderVideo and FinalVideoQa results.

## Known issues and next task

- `FinalVideoQa` `Job.Result` is persisted but not deserialized by `GET /api/jobs/{jobId}` (`GenerateIdeaWorkflow.FindJobAsync` omits `JobType.FinalVideoQa`); read it from PostgreSQL until a focused fix is authorised.
- Next milestone: manual publishing.
