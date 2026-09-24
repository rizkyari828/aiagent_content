#!/usr/bin/env bash
#
# Start the local AI Studio stack: PostgreSQL (existing compose.yaml), ComfyUI,
# and the AIStudio API. Operator-friendly and idempotent: an already-healthy
# service is never started twice.
#
# This script only starts/checks services. It never enqueues jobs, so it does
# not trigger GenerateAudio, image generation, rendering, or GPU inference.
# Starting ComfyUI itself is allowed, but no workflow is submitted to it.
#
# Usage:
#   ./scripts/start-local-stack.sh
#
# Optional environment overrides:
#   AISTUDIO_STACK_TIMEOUT   health wait per service, seconds   (default 600)
#   AISTUDIO_STACK_INTERVAL  health probe interval, seconds     (default 3)
#   AISTUDIO_API_PORT        API port                           (default 5080)
#   AISTUDIO_COMFYUI_PORT    ComfyUI port                       (default 8188)
#   AISTUDIO_COMFYUI_DIR     ComfyUI directory
#   AISTUDIO_COMFYUI_PYTHON  ComfyUI python executable
#
# Runtime files (pid/log) live in ai-studio/.runtime/ (git-ignored).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/local-stack.sh
. "$SCRIPT_DIR/lib/local-stack.sh"

usage() { sed -n '3,22p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; }

case "${1:-}" in
    -h|--help) usage; exit 0 ;;
    "") ;;
    *) die "Unknown argument: $1 (use --help)." ;;
esac

# --- Preconditions -----------------------------------------------------------
[ -f "$COMPOSE_FILE" ] || die "compose file not found: $COMPOSE_FILE"
[ -f "$ENV_FILE" ] || die ".env not found at $ENV_FILE (copy .env.example and fill it in)."
compose_available || die "Docker Compose is not available. Start Docker Desktop / enable WSL integration first."
command -v dotnet >/dev/null 2>&1 || die "dotnet is not on PATH."
command -v curl >/dev/null 2>&1 || die "curl is required."

mkdir_runtime

# Load project config (connection string, Postgres credentials) exactly like the
# existing scripts do, then apply the stack's required environment explicitly.
set -a
# shellcheck disable=SC1090
. "$ENV_FILE"
set +a

export ASPNETCORE_ENVIRONMENT="Development"
export ASPNETCORE_URLS="$API_BASE_URL"
export JobWorker__Enabled="true"
export ComfyUi__Enabled="true"

export SpeechSynthesis__Enabled="true"
export SpeechSynthesis__VoxCpm2__PythonExecutable="$HOME/Developer/Microservices/ai/voxcpm2/.venv/bin/python3"
export SpeechSynthesis__VoxCpm2__ModelPath="$HOME/Developer/Microservices/ai/voxcpm2/models/VoxCPM2"

export MusicGeneration__Enabled="true"
export MusicGeneration__AceStep__PythonExecutable="$HOME/Developer/Microservices/ai/ACE-Step-1.5/.venv/bin/python3"
export MusicGeneration__AceStep__ProjectRoot="$HOME/Developer/Microservices/ai/ACE-Step-1.5"

say "=============================================================="
say " Starting local AI Studio stack"
say "=============================================================="
say " repo:     $STACK_REPO_ROOT"
say " runtime:  $RUNTIME_DIR"
say " API:      $API_BASE_URL"
say " ComfyUI:  $COMFYUI_BASE_URL"
say ""

# --- 1. PostgreSQL (existing Docker Compose) ---------------------------------
log "PostgreSQL: docker compose up -d $COMPOSE_SERVICE"
compose up -d "$COMPOSE_SERVICE" || die "failed to start PostgreSQL via Docker Compose."
wait_for_postgres "$START_TIMEOUT" \
    || die "PostgreSQL did not become healthy within ${START_TIMEOUT}s."

# --- 2. ComfyUI --------------------------------------------------------------
COMFYUI_STATE=""
if http_healthy "$COMFYUI_STATS_URL"; then
    if cpid="$(read_pid "$COMFYUI_PID_FILE")" && pid_alive "$cpid"; then
        log "ComfyUI: already healthy (pid $cpid)"
        COMFYUI_STATE="healthy (pid $cpid)"
    else
        log "ComfyUI: already healthy (external process, not started by this script)"
        COMFYUI_STATE="healthy (external)"
    fi
else
    if cpid="$(read_pid "$COMFYUI_PID_FILE")" && pid_alive "$cpid" && pid_matches "$cpid" "main.py"; then
        log "ComfyUI: start already in progress (pid $cpid); waiting ..."
        COMFYUI_STATE="starting (pid $cpid)"
    else
        [ -x "$COMFYUI_PYTHON" ] || die "ComfyUI python not found or not executable: $COMFYUI_PYTHON"
        [ -f "$COMFYUI_DIR/main.py" ] || die "ComfyUI main.py not found in $COMFYUI_DIR"
        log "ComfyUI: starting $COMFYUI_PYTHON main.py --listen $COMFYUI_HOST --port $COMFYUI_PORT"
        start_detached "$COMFYUI_PID_FILE" "$COMFYUI_LOG_FILE" "$COMFYUI_DIR" \
            "$COMFYUI_PYTHON" main.py --listen "$COMFYUI_HOST" --port "$COMFYUI_PORT"
        cpid="$(read_pid "$COMFYUI_PID_FILE" || true)"
        COMFYUI_STATE="started (pid ${cpid:-?})"
    fi
    wait_for_health "ComfyUI" "$COMFYUI_STATS_URL" "$START_TIMEOUT" \
        || { dump_log_tail "ComfyUI" "$COMFYUI_LOG_FILE"; die "ComfyUI did not become healthy within ${START_TIMEOUT}s."; }
fi

# --- 3. AIStudio API ---------------------------------------------------------
API_STATE=""
if http_healthy "$API_READY_URL"; then
    if apid="$(read_pid "$API_PID_FILE")" && pid_alive "$apid"; then
        log "AIStudio API: already healthy (pid $apid)"
        API_STATE="healthy (pid $apid)"
    else
        log "AIStudio API: already healthy (external process, not started by this script)"
        API_STATE="healthy (external)"
    fi
else
    if apid="$(read_pid "$API_PID_FILE")" && pid_alive "$apid" && pid_matches "$apid" "AIStudio.Api"; then
        log "AIStudio API: start already in progress (pid $apid); waiting ..."
        API_STATE="starting (pid $apid)"
    else
        log "AIStudio API: starting dotnet run --project src/AIStudio.Api --no-launch-profile"
        start_detached "$API_PID_FILE" "$API_LOG_FILE" "$STACK_REPO_ROOT" \
            dotnet run --project src/AIStudio.Api --no-launch-profile
        apid="$(read_pid "$API_PID_FILE" || true)"
        API_STATE="started (pid ${apid:-?})"
    fi
    wait_for_health "AIStudio API" "$API_READY_URL" "$START_TIMEOUT" \
        || { dump_log_tail "AIStudio API" "$API_LOG_FILE"; die "AIStudio API did not become ready within ${START_TIMEOUT}s."; }
fi

# --- Final status ------------------------------------------------------------
say ""
say "=============================================================="
say " Local stack status"
say "=============================================================="
printf '  %-14s %s\n' "PostgreSQL" "$(postgres_health)"
printf '  %-14s %s\n' "ComfyUI" "$COMFYUI_STATE"
printf '  %-14s %s\n' "AIStudio API" "$API_STATE"
say ""
say " logs:    $RUNTIME_DIR/{api,comfyui}.log"
say " pids:    $RUNTIME_DIR/{api,comfyui}.pid"
say " stop:    ./scripts/stop-local-stack.sh"
say " status:  ./scripts/status-local-stack.sh"
