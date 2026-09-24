#!/usr/bin/env bash
#
# Shared helpers for the local AI Studio stack scripts.
# Sourced by start-local-stack.sh / stop-local-stack.sh / status-local-stack.sh.
# This file must not run anything on its own.

LOCAL_STACK_LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
STACK_REPO_ROOT="$(cd "$LOCAL_STACK_LIB_DIR/../.." && pwd)"   # -> ai-studio/

RUNTIME_DIR="${AISTUDIO_RUNTIME_DIR:-$STACK_REPO_ROOT/.runtime}"
API_PID_FILE="$RUNTIME_DIR/api.pid"
API_LOG_FILE="$RUNTIME_DIR/api.log"
COMFYUI_PID_FILE="$RUNTIME_DIR/comfyui.pid"
COMFYUI_LOG_FILE="$RUNTIME_DIR/comfyui.log"

# --- ComfyUI -----------------------------------------------------------------
COMFYUI_HOST="${AISTUDIO_COMFYUI_HOST:-127.0.0.1}"
COMFYUI_PORT="${AISTUDIO_COMFYUI_PORT:-8188}"
COMFYUI_BASE_URL="http://$COMFYUI_HOST:$COMFYUI_PORT"
COMFYUI_STATS_URL="$COMFYUI_BASE_URL/system_stats"
COMFYUI_DIR="${AISTUDIO_COMFYUI_DIR:-$HOME/Developer/Microservices/ai/ComfyUI}"
COMFYUI_PYTHON="${AISTUDIO_COMFYUI_PYTHON:-$COMFYUI_DIR/.venv/bin/python}"

# --- AIStudio API ------------------------------------------------------------
API_HOST="${AISTUDIO_API_HOST:-127.0.0.1}"
API_PORT="${AISTUDIO_API_PORT:-5080}"
API_BASE_URL="http://$API_HOST:$API_PORT"
API_READY_URL="$API_BASE_URL/health/ready"

# --- PostgreSQL (existing compose.yaml) --------------------------------------
COMPOSE_FILE="$STACK_REPO_ROOT/compose.yaml"
COMPOSE_SERVICE="postgres"
ENV_FILE="$STACK_REPO_ROOT/.env"

START_TIMEOUT="${AISTUDIO_STACK_TIMEOUT:-600}"
PROBE_INTERVAL="${AISTUDIO_STACK_INTERVAL:-3}"

log()  { printf '[stack] %s\n' "$*"; }
say()  { printf '%s\n' "$*"; }
warn() { printf '[stack] WARN: %s\n' "$*" >&2; }
die()  { printf '[stack] ERROR: %s\n' "$*" >&2; exit 1; }

mkdir_runtime() { mkdir -p "$RUNTIME_DIR"; }

# http_healthy <url> : true when the endpoint answers with a 2xx.
http_healthy() {
    curl -fsS -o /dev/null --max-time 5 "$1" >/dev/null 2>&1
}

# read_pid <pidfile> : prints pid, fails if missing/empty/non-numeric.
read_pid() {
    local file="$1" pid
    [ -f "$file" ] || return 1
    pid="$(cat "$file" 2>/dev/null || true)"
    [[ "$pid" =~ ^[0-9]+$ ]] || return 1
    printf '%s' "$pid"
}

pid_alive() {
    [ -n "${1:-}" ] && kill -0 "$1" 2>/dev/null
}

# pid_matches <pid> <substring> : true when /proc/<pid>/cmdline contains the
# substring. Cheap guard so stop never kills an unrelated process that reused
# a stale pid.
pid_matches() {
    local pid="$1" needle="$2" cmdline
    [ -r "/proc/$pid/cmdline" ] || return 1
    cmdline="$(tr '\0' ' ' <"/proc/$pid/cmdline" 2>/dev/null || true)"
    [[ "$cmdline" == *"$needle"* ]]
}

# start_detached <pidfile> <logfile> <workdir> <cmd...>
# Starts in its own session (setsid) so stop can signal the whole process group
# without touching unrelated processes; records the session-leader pid.
start_detached() {
    local pidfile="$1" logfile="$2" workdir="$3"; shift 3
    mkdir_runtime
    : >"$logfile"
    (
        cd "$workdir" || exit 1
        setsid "$@" >>"$logfile" 2>&1 </dev/null &
        echo $! >"$pidfile"
    )
}

# wait_for_health <label> <url> <timeout-seconds>
wait_for_health() {
    local label="$1" url="$2" timeout="$3"
    local deadline=$((SECONDS + timeout))
    log "$label: waiting for $url (up to ${timeout}s) ..."
    while :; do
        if http_healthy "$url"; then
            log "$label: healthy"
            return 0
        fi
        [ "$SECONDS" -lt "$deadline" ] || return 1
        sleep "$PROBE_INTERVAL"
    done
}

# dump_log_tail <label> <logfile>
dump_log_tail() {
    local label="$1" logfile="$2"
    [ -f "$logfile" ] || return 0
    warn "$label log tail ($logfile):"
    tail -n 20 "$logfile" >&2 || true
}

# --- Docker Compose (existing compose.yaml) ----------------------------------
compose_available() {
    command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1
}

compose() {
    ( cd "$STACK_REPO_ROOT" && docker compose -f "$COMPOSE_FILE" "$@" )
}

postgres_container_id() {
    compose ps -q "$COMPOSE_SERVICE" 2>/dev/null | head -n 1
}

# postgres_health : prints healthy / unhealthy / starting / running / unknown / absent.
postgres_health() {
    local cid
    if ! compose_available; then
        printf 'docker-unavailable'
        return
    fi
    cid="$(postgres_container_id)"
    if [ -z "$cid" ]; then
        printf 'absent'
        return
    fi
    docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' \
        "$cid" 2>/dev/null || printf 'unknown'
}

wait_for_postgres() {
    local timeout="$1" deadline=$((SECONDS + $1)) status
    log "PostgreSQL: waiting for container health (up to ${timeout}s) ..."
    while :; do
        status="$(postgres_health)"
        case "$status" in
            healthy|running)
                log "PostgreSQL: $status"
                return 0
                ;;
        esac
        [ "$SECONDS" -lt "$deadline" ] || return 1
        sleep "$PROBE_INTERVAL"
    done
}
