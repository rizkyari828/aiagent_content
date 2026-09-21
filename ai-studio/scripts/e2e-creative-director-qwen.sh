#!/usr/bin/env bash
#
# Creative Director Real-Qwen Validation v1 — one-command runner.
#
# Drives the PRODUCTION ICreativeDirector (+ the real local Ollama provider) over
# five representative ApprovedIdea cases, verifies the deterministic validation
# dimensions (JSON/schema/idea/audience/recipe/leak), preserves raw artifacts, and
# prints a concise report. No media generation, no database, no model downloads.
#
# The real multi-case Qwen run is USER-EXECUTED. This script only orchestrates it.
#
# Usage:
#   scripts/e2e-creative-director-qwen.sh
#
# Configuration is the repository's existing local convention (sourced from .env):
#   Ollama__BaseUrl          default http://127.0.0.1:11434
#   Ollama__DefaultModel     default qwen3.8:27b-q4_K_M (must be installed locally)
#   Ollama__TimeoutSeconds   default 300 (1..600), applied as the per-request timeout
#   SpeechSynthesis__Enabled / MusicGeneration__Enabled / Manim__Enabled /
#   ComfyUi__Enabled / Blender__Enabled  capability availability (default false);
#   enable them to validate recipe resolution against a fully enabled local stack.
#   AISTUDIO_VALIDATION_OUTPUT   artifact directory override.
#
# Exit codes: 0 for a completed run (even with per-case REVIEW/FAIL findings),
#             1 for a runner/infrastructure failure or a total validation failure.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HARNESS_PROJECT="$REPO_ROOT/.demo/CreativeDirectorQwenValidation/AIStudio.CreativeValidation.csproj"
HARNESS_DLL="$REPO_ROOT/.demo/CreativeDirectorQwenValidation/bin/Release/net10.0/AIStudio.CreativeValidation.dll"

say() { printf '%s\n' "$*"; }
die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }

command -v curl >/dev/null || die "curl is required."
command -v dotnet >/dev/null || die "dotnet is required."
command -v jq >/dev/null || die "jq is required."

# Load local environment configuration the same way the API and other e2e scripts do.
if [ -f "$REPO_ROOT/.env" ]; then
  set -a
  # shellcheck disable=SC1091
  . "$REPO_ROOT/.env"
  set +a
fi

BASE_URL="${Ollama__BaseUrl:-http://127.0.0.1:11434}"
MODEL="${Ollama__DefaultModel:-qwen3.8:27b-q4_K_M}"
TIMEOUT_SECONDS="${Ollama__TimeoutSeconds:-300}"

export Ollama__BaseUrl="$BASE_URL"
export Ollama__DefaultModel="$MODEL"
export Ollama__TimeoutSeconds="$TIMEOUT_SECONDS"

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUTPUT_DIR="${AISTUDIO_VALIDATION_OUTPUT:-$REPO_ROOT/.demo/artifacts/creative-director-qwen-$STAMP}"
mkdir -p "$OUTPUT_DIR"

TAGS_FILE="$(mktemp)"
trap 'rm -f "$TAGS_FILE"' EXIT

say "Preflight"
say "  ollama: $BASE_URL"
say "  model:  $MODEL"

# Provider reachability: the configured local model must already be installed.
# This validation never downloads a model or starts another inference server.
curl -sf "$BASE_URL/api/tags" -o "$TAGS_FILE" \
  || die "Ollama is not reachable at $BASE_URL. Start the local service, then retry."

if ! jq -e --arg model "$MODEL" \
  'any(.models[]?; (.name == $model) or (.model == $model))' "$TAGS_FILE" >/dev/null; then
  die "Model '$MODEL' is not available in Ollama. Run 'ollama list' and set Ollama__DefaultModel to an installed model; do not download a new model for this validation."
fi

say "  model available: yes"
say ""

say "Build"
if ! dotnet build "$HARNESS_PROJECT" -c Release --nologo -v quiet; then
  die "Harness build failed."
fi
say "  harness built: yes"
say ""

if ! dotnet "$HARNESS_DLL" --self-check; then
  die "Harness self-check failed."
fi
say ""

set +e
dotnet "$HARNESS_DLL" --output "$OUTPUT_DIR"
CODE=$?
set -e

exit "$CODE"
