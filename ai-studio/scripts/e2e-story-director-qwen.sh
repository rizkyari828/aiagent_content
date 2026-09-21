#!/usr/bin/env bash
#
# Real Qwen Story Director Validation v1 — one-command runner.
#
# Feeds the five REAL CreativeDirection artifacts produced by the Creative Director
# real-Qwen validation into the PRODUCTION Qwen-backed Story Director
# (StoryDirector:Mode=qwen → IStoryDirector → QwenStoryDirector), preserves raw
# artifacts, and prints a concise report. No media generation, no database, no
# Creative Director rerun, no model download.
#
# The real five-case Story Director run is USER-EXECUTED. This script orchestrates it.
#
# Usage:
#   scripts/e2e-story-director-qwen.sh
#
# Configuration uses the repository's existing local convention (sourced from .env):
#   Ollama__BaseUrl          default http://127.0.0.1:11434
#   Ollama__DefaultModel     default qwen3.8:27b-q4_K_M (must be installed locally)
#   Ollama__TimeoutSeconds   default 300 (1..600) per-request timeout
#   AISTUDIO_CREATIVE_DIRECTIONS   upstream CreativeDirection artifacts dir
#   AISTUDIO_VALIDATION_OUTPUT     artifact directory override
#
# StoryDirector__Mode=qwen is set for THIS runner process only; the application
# default stays deterministic.
#
# Exit codes: 0 for a completed run (even with per-case REVIEW/FAIL findings),
#             1 for a total validation failure, 2 for a runner/infrastructure failure.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HARNESS_PROJECT="$REPO_ROOT/.demo/StoryDirectorQwenValidation/AIStudio.StoryValidation.csproj"
HARNESS_DLL="$REPO_ROOT/.demo/StoryDirectorQwenValidation/bin/Release/net10.0/AIStudio.StoryValidation.dll"
DEFAULT_DIRECTIONS="$REPO_ROOT/.demo/artifacts/creative-director-qwen-20260921T125832Z"

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

# Force qwen mode for this runner process only; do not modify .env or appsettings.
export StoryDirector__Mode=qwen

BASE_URL="${Ollama__BaseUrl:-http://127.0.0.1:11434}"
MODEL="${Ollama__DefaultModel:-qwen3.8:27b-q4_K_M}"
TIMEOUT_SECONDS="${Ollama__TimeoutSeconds:-300}"
export Ollama__BaseUrl="$BASE_URL"
export Ollama__DefaultModel="$MODEL"
export Ollama__TimeoutSeconds="$TIMEOUT_SECONDS"

DIRECTIONS_DIR="${AISTUDIO_CREATIVE_DIRECTIONS:-$DEFAULT_DIRECTIONS}"
export AISTUDIO_CREATIVE_DIRECTIONS="$DIRECTIONS_DIR"

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUTPUT_DIR="${AISTUDIO_VALIDATION_OUTPUT:-$REPO_ROOT/.demo/artifacts/story-director-qwen-$STAMP}"
mkdir -p "$OUTPUT_DIR"

TAGS_FILE="$(mktemp)"
trap 'rm -f "$TAGS_FILE"' EXIT

say "Preflight"
say "  ollama:   $BASE_URL"
say "  model:    $MODEL"
say "  mode:     $StoryDirector__Mode"
say "  input:    $DIRECTIONS_DIR"

# Provider reachability: the configured local model must already be installed.
# This validation never downloads a model or starts another inference server.
curl -sf "$BASE_URL/api/tags" -o "$TAGS_FILE" \
  || die "Ollama is not reachable at $BASE_URL. Start the local service, then retry."

if ! jq -e --arg model "$MODEL" \
  'any(.models[]?; (.name == $model) or (.model == $model))' "$TAGS_FILE" >/dev/null; then
  die "Model '$MODEL' is not available in Ollama. Run 'ollama list' and set Ollama__DefaultModel to an installed model; do not download a new model for this validation."
fi

# Upstream real-Qwen CreativeDirection inputs must already exist; never regenerate them.
[ -d "$DIRECTIONS_DIR" ] || die "CreativeDirection artifacts missing: $DIRECTIONS_DIR. Run ./scripts/e2e-creative-director-qwen.sh first (or set AISTUDIO_CREATIVE_DIRECTIONS)."

MISSING=0
for case_dir in \
  case-1-tech-explainer \
  case-2-anime-story \
  case-3-preschool-3d-story \
  case-4-photoreal-product-short \
  case-5-pet-mascot-commercial; do
  if [ ! -f "$DIRECTIONS_DIR/$case_dir/creative-direction.json" ]; then
    say "  missing: $case_dir/creative-direction.json"
    MISSING=1
  fi
done
[ "$MISSING" -eq 0 ] || die "One or more CreativeDirection artifacts are missing under $DIRECTIONS_DIR."
say "  inputs:   5/5 present"
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
dotnet "$HARNESS_DLL" --directions "$DIRECTIONS_DIR" --output "$OUTPUT_DIR"
CODE=$?
set -e

exit "$CODE"
