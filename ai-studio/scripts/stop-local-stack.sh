#!/usr/bin/env bash
#
# Stop the AIStudio API and ComfyUI processes started by start-local-stack.sh.
# PostgreSQL (Docker Compose) is left running by default.
#
# It stops only processes recorded in this script's own pid files, and only
# after verifying the pid still looks like the expected process, so unrelated
# processes are never killed. All STOPPED processes are signaled by process
# group (they were started with setsid).
#
# Usage:
#   ./scripts/stop-local-stack.sh          # stop API + ComfyUI
#   ./scripts/stop-local-stack.sh --all    # also stop the Docker Compose stack
#
# Optional environment overrides:
#   AISTUDIO_RUNTIME_DIR   pid/log directory (default ai-studio/.runtime)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/local-stack.sh
. "$SCRIPT_DIR/lib/local-stack.sh"

STOP_ALL="false"
case "${1:-}" in
    -h|--help)
        sed -n '3,16p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
        exit 0
        ;;
    --all) STOP_ALL="true" ;;
    "") ;;
    *) die "Unknown argument: $1 (use --help)." ;;
esac

# stop_pidfile <label> <pidfile> <expected cmdline substring>
stop_pidfile() {
    local label="$1" pidfile="$2" expected="$3" pid
    if ! pid="$(read_pid "$pidfile")"; then
        rm -f "$pidfile"
        log "$label: not running (no pid file)"
        return 0
    fi
    if ! pid_alive "$pid"; then
        rm -f "$pidfile"
        log "$label: not running (stale pid $pid)"
        return 0
    fi
    if ! pid_matches "$pid" "$expected"; then
        warn "$label: pid $pid does not look like '$expected'; refusing to kill it."
        warn "       inspect $pidfile and remove it manually if it is stale."
        return 1
    fi

    log "$label: stopping pid $pid ..."
    kill -TERM -- "-$pid" 2>/dev/null || kill -TERM "$pid" 2>/dev/null || true
    local deadline=$((SECONDS + 15))
    while pid_alive "$pid" && [ "$SECONDS" -lt "$deadline" ]; do
        sleep 1
    done
    if pid_alive "$pid"; then
        warn "$label: still running; sending SIGKILL"
        kill -KILL -- "-$pid" 2>/dev/null || kill -KILL "$pid" 2>/dev/null || true
        sleep 1
    fi

    rm -f "$pidfile"
    log "$label: stopped"
}

say "=============================================================="
say " Stopping local AI Studio stack"
say "=============================================================="

stop_pidfile "AIStudio API" "$API_PID_FILE" "AIStudio.Api" || true
stop_pidfile "ComfyUI" "$COMFYUI_PID_FILE" "main.py" || true

if [ "$STOP_ALL" = "true" ]; then
    if compose_available; then
        log "PostgreSQL: docker compose down"
        compose down || warn "docker compose down failed."
    else
        warn "Docker Compose is not available; PostgreSQL left untouched."
    fi
else
    log "PostgreSQL: left running (use --all to stop Docker Compose dependencies)"
fi
