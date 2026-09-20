#!/usr/bin/env bash
#
# Narrative Sync v1 — deterministic manual end-to-end validation.
#
# Runs the real production path for the canonical Video #1 project:
#   GenerateAudio (reviewed-script per-scene VoxCPM2 narration + ACE-Step BGM + mix)
#     -> GenerateSceneVisuals (speech-driven durations + narration windows)
#       -> RenderVideo?variant=narrative-sync-v1
#         -> FinalVideoQa
# then probes the resulting MP4 with ffprobe.
#
# Expensive local AI / rendering is USER-EXECUTED: this script orchestrates it.
# It polls the durable jobs, prints a concise summary, and never touches the
# canonical `{storyboard}.mp4` or `-motion-v1.mp4` artifacts.
#
# Usage:
#   scripts/e2e-narrative-sync.sh
#
# Common overrides (all optional):
#   AISTUDIO_PROJECT_ID / AISTUDIO_STORYBOARD_JOB_ID   target project (defaults to Video #1)
#   AISTUDIO_VARIANT                                   review suffix (default: narrative-sync-v1)
#   AISTUDIO_API_URL                                   API base (default: .env ASPNETCORE_URLS)
#   AISTUDIO_START_API=auto|1|0                        start the API when unreachable (default: auto)
#   AISTUDIO_MANIM=1 AISTUDIO_COMFYUI=1 AISTUDIO_BLENDER=0   visual engines to enable
#   AISTUDIO_FORCE_VISUALS=0                           force visual regeneration
#   AISTUDIO_VOXCPM_PYTHON / AISTUDIO_VOXCPM_MODEL
#   AISTUDIO_ACESTEP_PYTHON / AISTUDIO_ACESTEP_ROOT
#   AISTUDIO_POLL_TIMEOUT (seconds, default 10800) / AISTUDIO_POLL_INTERVAL (default 5)
#
# Exit codes: 0 = all stages succeeded and QA passed; non-zero = first failure.
set -euo pipefail

log() { printf '[e2e] %s\n' "$*" >&2; }
say() { printf '%s\n' "$*"; }
die() { printf '[e2e] ERROR: %s\n' "$*" >&2; exit 1; }

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
API_ROOT="$REPO_ROOT/src/AIStudio.Api"
ASSETS_ROOT="${AISTUDIO_ASSETS_ROOT:-$API_ROOT/assets}"
LOG_FILE="${AISTUDIO_E2E_LOG:-$REPO_ROOT/scripts/e2e-narrative-sync.log}"

# --- target -----------------------------------------------------------------
PROJECT_ID="${AISTUDIO_PROJECT_ID:-01b7f0ef-e749-4e1d-bbad-a93d016b2be2}"
STORYBOARD_JOB_ID="${AISTUDIO_STORYBOARD_JOB_ID:-1526be4b-6d43-4f54-9997-82cc368a1990}"
VARIANT="${AISTUDIO_VARIANT:-narrative-sync-v1}"
POLL_INTERVAL="${AISTUDIO_POLL_INTERVAL:-5}"
POLL_TIMEOUT="${AISTUDIO_POLL_TIMEOUT:-10800}"
FORCE_VISUALS="${AISTUDIO_FORCE_VISUALS:-0}"

# --- provider runtime (auto-detected local installs) ------------------------
VOXCPM_PYTHON="${AISTUDIO_VOXCPM_PYTHON:-$HOME/Developer/Microservices/ai/voxcpm2/.venv/bin/python3}"
VOXCPM_MODEL="${AISTUDIO_VOXCPM_MODEL:-$HOME/Developer/Microservices/ai/voxcpm2/models/VoxCPM2}"
ACESTEP_PYTHON="${AISTUDIO_ACESTEP_PYTHON:-$HOME/Developer/Microservices/ai/ACE-Step-1.5/.venv/bin/python3}"
ACESTEP_ROOT="${AISTUDIO_ACESTEP_ROOT:-$HOME/Developer/Microservices/ai/ACE-Step-1.5}"
MANIM_PYTHON="${AISTUDIO_MANIM_PYTHON:-$REPO_ROOT/.venv/bin/python3}"

# --- engine toggles ---------------------------------------------------------
bool() { [ "$1" = 1 ] && echo true || echo false; }
ENABLE_MANIM="${AISTUDIO_MANIM:-1}"
ENABLE_COMFYUI="${AISTUDIO_COMFYUI:-1}"
ENABLE_BLENDER="${AISTUDIO_BLENDER:-0}"

# --- environment ------------------------------------------------------------
if [ -f "$REPO_ROOT/.env" ]; then
  set -a
  # shellcheck disable=SC1091
  . "$REPO_ROOT/.env"
  set +a
fi
API_URL="${AISTUDIO_API_URL:-${ASPNETCORE_URLS:-http://127.0.0.1:5002}}"
API_URL="${API_URL%/}"
PG_CONTAINER="${AISTUDIO_PG_CONTAINER:-aistudio-dev-postgres-1}"
PG_USER="${POSTGRES_USER:-aistudio}"
PG_DB="${POSTGRES_DB:-aistudio}"

command -v curl >/dev/null || die "curl is required."
command -v jq >/dev/null || die "jq is required."
command -v ffprobe >/dev/null || die "ffprobe is required."

API_PID=""
cleanup() {
  if [ -n "$API_PID" ] && kill -0 "$API_PID" 2>/dev/null; then
    log "stopping API (pid $API_PID)"
    kill "$API_PID" 2>/dev/null || true
    wait "$API_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT

api_ready() { curl -sf -o /dev/null "$API_URL/health/ready"; }

start_api() {
  local mode="${AISTUDIO_START_API:-auto}"
  if api_ready; then
    log "API already healthy at $API_URL (provider flags below are NOT applied to it)"
    return 0
  fi
  if [ "$mode" = "0" ]; then
    die "API is not reachable at $API_URL and AISTUDIO_START_API=0."
  fi
  log "building Release (if needed) ..."
  dotnet build "$REPO_ROOT/AIStudio.slnx" -c Release >"$LOG_FILE" 2>&1 \
    || { tail -n 40 "$LOG_FILE" >&2; die "Release build failed (log: $LOG_FILE)"; }

  log "starting API at $API_URL (log: $LOG_FILE)"
  (
    cd "$API_ROOT"
    env \
      "ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-Development}" \
      "SpeechSynthesis__Enabled=true" \
      "SpeechSynthesis__VoxCpm2__PythonExecutable=$VOXCPM_PYTHON" \
      "SpeechSynthesis__VoxCpm2__ModelPath=$VOXCPM_MODEL" \
      "MusicGeneration__Enabled=true" \
      "MusicGeneration__AceStep__PythonExecutable=$ACESTEP_PYTHON" \
      "MusicGeneration__AceStep__ProjectRoot=$ACESTEP_ROOT" \
      "Manim__Enabled=$(bool "$ENABLE_MANIM")" \
      "Manim__PythonPath=$MANIM_PYTHON" \
      "ComfyUi__Enabled=$(bool "$ENABLE_COMFYUI")" \
      "Blender__Enabled=$(bool "$ENABLE_BLENDER")" \
      dotnet "bin/Release/net10.0/AIStudio.Api.dll"
  ) >>"$LOG_FILE" 2>&1 &
  API_PID=$!

  local deadline=$((SECONDS + 180))
  until api_ready; do
    if ! kill -0 "$API_PID" 2>/dev/null; then
      tail -n 40 "$LOG_FILE" >&2
      die "API exited during startup (log: $LOG_FILE)"
    fi
    [ "$SECONDS" -lt "$deadline" ] || { tail -n 40 "$LOG_FILE" >&2; die "API did not become ready (log: $LOG_FILE)"; }
    sleep 2
  done
  log "API ready"
}

# --- job helpers ------------------------------------------------------------
get_job()  { curl -sf "$API_URL/api/jobs/$1"; }
db_result() { docker exec "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -tAc \
  "SELECT result FROM jobs WHERE id = '$1'" 2>/dev/null || true; }

enqueue() { # <url>
  curl -sS -X POST "$1"
}

wait_job() { # <jobId> <label> -> prints final GET JSON on stdout
  local job_id="$1" label="$2" started=$SECONDS deadline=$((SECONDS + POLL_TIMEOUT)) json status
  while :; do
    if ! json="$(get_job "$job_id")"; then
      [ "$SECONDS" -lt "$deadline" ] || die "$label: gave up polling after ${POLL_TIMEOUT}s."
      sleep "$POLL_INTERVAL"; continue
    fi
    status="$(jq -r '.status' <<<"$json")"
    case "$status" in
      completed)
        log "$label completed in $((SECONDS - started))s"
        printf '%s' "$json"; return 0;;
      failed|cancelled)
        say "  ! $label $status: $(jq -r '.errorCode // "?"' <<<"$json"): $(jq -r '.errorSummary // ""' <<<"$json")"
        [ -f "$LOG_FILE" ] && say "  ! API log: $LOG_FILE"
        return 1;;
    esac
    [ "$SECONDS" -lt "$deadline" ] || { say "  ! $label timed out after ${POLL_TIMEOUT}s"; return 1; }
    sleep "$POLL_INTERVAL"
  done
}

run_stage() { # <label> <enqueue-url>
  local label="$1" url="$2" response job_id
  log "enqueue $label"
  response="$(enqueue "$url" || true)"
  job_id="$(jq -r '.jobId // empty' <<<"$response" 2>/dev/null || true)"
  if [ -z "$job_id" ]; then
    say "  ! $label enqueue rejected: $(tr -d '\n' <<<"$response" | cut -c1-400)"
    [ -f "$LOG_FILE" ] && say "  ! API log: $LOG_FILE"
    return 1
  fi
  log "$label job=$job_id"
  wait_job "$job_id" "$label"
}

# ---------------------------------------------------------------------------
start_api

say "Narrative Sync v1 E2E"
say "project=$PROJECT_ID storyboard=$STORYBOARD_JOB_ID variant=$VARIANT"
say "engines: manim=$(bool "$ENABLE_MANIM") comfyui=$(bool "$ENABLE_COMFYUI") blender=$(bool "$ENABLE_BLENDER")"
say "providers: voxcpm=$VOXCPM_PYTHON acestep=$ACESTEP_PYTHON"
say ""

# Pre-flight: surface jobs already queued/running for this project. If a previous
# session left one behind, the worker will recover and run it; a new job will be
# enqueued too. This is informational only.
ACTIVE="$(docker exec "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" -tAc \
  "SELECT job_type || ' ' || id || ' (' || status || ')' FROM jobs WHERE content_project_id = '$PROJECT_ID' AND status IN ('Queued','Running') ORDER BY created_at;" 2>/dev/null || true)"
if [ -n "$ACTIVE" ]; then
  say "!! active jobs for this project (worker will also process these):"
  while IFS= read -r line; do say "   $line"; done <<<"$ACTIVE"
  say ""
fi

# --- 1. Audio (per-scene narration + BGM + master) --------------------------
AUDIO_JSON="$(run_stage GenerateAudio \
  "$API_URL/api/content-projects/$PROJECT_ID/storyboard-jobs/$STORYBOARD_JOB_ID/audio-jobs")" || exit 1
AUDIO_RESULT="$(jq -c '.result' <<<"$AUDIO_JSON")"

say "== Narration =="
say "  scenes: $(jq -r '.scenes | length' <<<"$AUDIO_RESULT")  reused: $(jq -r '[.scenes[] | select(.reused)] | length' <<<"$AUDIO_RESULT")"
jq -r '.scenes[] | "  scene \(.sceneIndex): narration=\(.narrationDurationSeconds)s visual=\(.visualDurationSeconds)s start=\(.narrationStartSeconds)s"' <<<"$AUDIO_RESULT"
say "  assembled narration: $(jq -r '.narration.durationSeconds' <<<"$AUDIO_RESULT")s reused=$(jq -r '.narrationReused' <<<"$AUDIO_RESULT")"
say "  bgm: $(jq -r '.music.durationSeconds' <<<"$AUDIO_RESULT")s reused=$(jq -r '.musicReused' <<<"$AUDIO_RESULT")"
say "  master: $(jq -r '.master.durationSeconds' <<<"$AUDIO_RESULT")s reused=$(jq -r '.masterReused' <<<"$AUDIO_RESULT")"
say "  transition: $(jq -r '.transitionSeconds' <<<"$AUDIO_RESULT")s"
say ""

# --- 2. Scene visuals (speech-driven timing + narration windows) ------------
VIS_JSON="$(run_stage GenerateSceneVisuals \
  "$API_URL/api/content-projects/$PROJECT_ID/storyboard-jobs/$STORYBOARD_JOB_ID/visual-jobs?force=$( [ "$FORCE_VISUALS" = 1 ] && echo true || echo false )")" || exit 1
VIS_JOB_ID="$(jq -r '.id' <<<"$VIS_JSON")"
VIS_RESULT="$(db_result "$VIS_JOB_ID")"

say "== Visuals =="
if [ -n "$VIS_RESULT" ]; then
  say "  generated=$(jq -r '.generatedCount' <<<"$VIS_RESULT") reused=$(jq -r '.skippedCount' <<<"$VIS_RESULT")"
  jq -r '.routing[]? | "  scene \(.sceneIndex): engine=\(.engine) intended=\(.intendedEngine) status=\(.status) template=\(.template)"' <<<"$VIS_RESULT"
else
  say "  (result not readable from PostgreSQL; see job $VIS_JOB_ID)"
fi
say ""

# --- 3. Render (narrative timing + derived subtitles) -----------------------
RENDER_JSON="$(run_stage RenderVideo \
  "$API_URL/api/content-projects/$PROJECT_ID/storyboard-jobs/$STORYBOARD_JOB_ID/render-jobs?variant=$VARIANT")" || exit 1
RENDER_JOB_ID="$(jq -r '.id' <<<"$RENDER_JSON")"
RENDER_RESULT="$(jq -c '.result' <<<"$RENDER_JSON")"
OUTPUT_REL="$(jq -r '.outputPath' <<<"$RENDER_RESULT")"
OUTPUT_ABS="$ASSETS_ROOT/$OUTPUT_REL"

say "== Render =="
say "  audioSource=$(jq -r '.audioSource' <<<"$RENDER_RESULT") subtitleSource=$(jq -r '.subtitleSource' <<<"$RENDER_RESULT") burnedIn=$(jq -r '.subtitleBurnedIn' <<<"$RENDER_RESULT")"
say "  duration=$(jq -r '.durationSeconds' <<<"$RENDER_RESULT")s size=$(jq -r '.byteSize' <<<"$RENDER_RESULT")B scenes=$(jq -r '.sceneCount' <<<"$RENDER_RESULT")"
say "  output=$OUTPUT_ABS"
say ""

# --- 4. Final QA ------------------------------------------------------------
QA_JSON="$(run_stage FinalVideoQa \
  "$API_URL/api/content-projects/$PROJECT_ID/render-jobs/$RENDER_JOB_ID/qa-jobs")" || exit 1
QA_JOB_ID="$(jq -r '.id' <<<"$QA_JSON")"
QA_RESULT="$(db_result "$QA_JOB_ID")"

say "== FinalVideoQa =="
if [ -n "$QA_RESULT" ]; then
  say "  pass video=$(jq -r '.hasVideo' <<<"$QA_RESULT") audio=$(jq -r '.hasAudio' <<<"$QA_RESULT") duration=$(jq -r '.durationSeconds' <<<"$QA_RESULT")s $(jq -r '.width' <<<"$QA_RESULT")x$(jq -r '.height' <<<"$QA_RESULT")"
else
  say "  status=completed (result not readable from PostgreSQL; job $QA_JOB_ID)"
fi
say ""

# --- 5. Media properties ----------------------------------------------------
say "== Media =="
if [ -f "$OUTPUT_ABS" ]; then
  ffprobe -v error -show_entries format=duration,size,bit_rate \
    -show_entries stream=codec_type,codec_name,width,height,sample_rate,channels \
    -of json "$OUTPUT_ABS" \
    | jq -r '"  container: \(.format.duration)s \(.format.size)B ~\(.format.bit_rate // "?" )bps", (.streams[] | "  stream \(.codec_type): \(.codec_name) \(.width // "-")x\(.height // "-") sr=\(.sample_rate // "-") ch=\(.channels // "-")")'
else
  say "  ! output file not found at $OUTPUT_ABS"
fi

say ""
say "REVIEW MP4: $OUTPUT_ABS"
