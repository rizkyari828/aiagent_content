#!/usr/bin/env bash
#
# Show the local AI Studio stack status: PostgreSQL (Docker Compose), ComfyUI,
# and the AIStudio API, plus the stored pid files.
#
# Read-only. It never starts or stops anything.
#
# Usage:
#   ./scripts/status-local-stack.sh
#
# Optional environment overrides: see scripts/lib/local-stack.sh.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/local-stack.sh
. "$SCRIPT_DIR/lib/local-stack.sh"

describe_pid() {
    local pidfile="$1" expected="$2" pid
    if ! pid="$(read_pid "$pidfile")"; then
        printf 'none'
        return
    fi
    if ! pid_alive "$pid"; then
        printf 'stale pid %s' "$pid"
        return
    fi
    if pid_matches "$pid" "$expected"; then
        printf 'pid %s (alive)' "$pid"
    else
        printf 'pid %s (alive, unexpected cmdline)' "$pid"
    fi
}

health_word() {
    if http_healthy "$1"; then printf 'healthy'; else printf 'unreachable'; fi
}

PG="$(postgres_health)"
COMFY_HEALTH="$(health_word "$COMFYUI_STATS_URL")"
API_HEALTH="$(health_word "$API_READY_URL")"

say "=============================================================="
say " Local AI Studio stack status"
say "=============================================================="
printf '  %-14s %-22s %s\n' "Service" "Health" "Detail"
printf '  %-14s %-22s %s\n' "PostgreSQL" "$PG" "docker compose: $COMPOSE_SERVICE"
printf '  %-14s %-22s %s\n' "ComfyUI" "$COMFY_HEALTH" "$COMFYUI_STATS_URL"
printf '  %-14s %-22s %s\n' "AIStudio API" "$API_HEALTH" "$API_READY_URL"
say ""
say "  pid files ($RUNTIME_DIR):"
printf '    %-12s %s\n' "API" "$(describe_pid "$API_PID_FILE" "AIStudio.Api")"
printf '    %-12s %s\n' "ComfyUI" "$(describe_pid "$COMFYUI_PID_FILE" "main.py")"
say "  logs:"
say "    $API_LOG_FILE"
say "    $COMFYUI_LOG_FILE"
