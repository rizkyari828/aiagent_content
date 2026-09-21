#!/usr/bin/env bash
#
# Real-Qwen Story Bible Planner Validation v1 — one-command runner.
#
# Drives the PRODUCTION IStoryBiblePlanner (QwenStoryBiblePlanner over the real
# OllamaTextGenerator) over the already validated real CreativeDirection + StoryPlan
# artifacts, validates the proposal with the production parser and validators, then
# applies it explicitly through the deterministic StoryPlanGrounder and
# StoryContextBuilder. It never registers anything in production, persists nothing,
# mutates no StoryPlan, and calls no Script/Storyboard/state-planning stage. No media,
# no database, no upstream rerun, no model download.
#
# Only two cases run in v1 (anime-story and preschool-3d-story) to validate the new
# contract cheaply. The real run is USER-EXECUTED. This script orchestrates it.
#
# Usage:
#   scripts/e2e-story-bible-qwen.sh
#
# Configuration uses the repository's local convention (sourced from .env):
#   Ollama__BaseUrl                default http://127.0.0.1:11434
#   Ollama__DefaultModel           default qwen3.8:27b-q4_K_M (must be installed locally)
#   Ollama__TimeoutSeconds         default 300 (1..600) per-request timeout
#   AISTUDIO_CREATIVE_DIRECTIONS   creative-direction artifacts dir
#   AISTUDIO_STORY_DIRECTIONS      story-plan artifacts dir
#   AISTUDIO_VALIDATION_OUTPUT     artifact directory override
#
# Exit codes: 0 for a completed run (even with per-case REVIEW/FAIL findings),
#             1 for a total validation failure, 2 for a runner/infrastructure failure.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HARNESS_PROJECT="$REPO_ROOT/.demo/StoryBibleQwenValidation/AIStudio.StoryBibleValidation.csproj"
HARNESS_DLL="$REPO_ROOT/.demo/StoryBibleQwenValidation/bin/Release/net10.0/AIStudio.StoryBibleValidation.dll"
DEFAULT_CREATIVE_DIR="$REPO_ROOT/.demo/artifacts/creative-director-qwen-20260921T125832Z"
DEFAULT_STORY_DIR="$REPO_ROOT/.demo/artifacts/story-director-qwen-20260921T135529Z"

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

CREATIVE_DIR="${AISTUDIO_CREATIVE_DIRECTIONS:-$DEFAULT_CREATIVE_DIR}"
STORY_DIR="${AISTUDIO_STORY_DIRECTIONS:-$DEFAULT_STORY_DIR}"
export AISTUDIO_CREATIVE_DIRECTIONS="$CREATIVE_DIR"
export AISTUDIO_STORY_DIRECTIONS="$STORY_DIR"

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUTPUT_DIR="${AISTUDIO_VALIDATION_OUTPUT:-$REPO_ROOT/.demo/artifacts/story-bible-qwen-$STAMP}"
mkdir -p "$OUTPUT_DIR"

TAGS_FILE="$(mktemp)"
trap 'rm -f "$TAGS_FILE"' EXIT

say "Preflight"
say "  ollama:   $BASE_URL"
say "  model:    $MODEL"
say "  creative: $CREATIVE_DIR"
say "  story:    $STORY_DIR"

# Provider reachability: the configured local model must already be installed.
# This validation never downloads a model or starts another inference server.
curl -sf "$BASE_URL/api/tags" -o "$TAGS_FILE" \
  || die "Ollama is not reachable at $BASE_URL. Start the local service, then retry."

if ! jq -e --arg model "$MODEL" \
  'any(.models[]?; (.name == $model) or (.model == $model))' "$TAGS_FILE" >/dev/null; then
  die "Model '$MODEL' is not available in Ollama. Run 'ollama list' and set Ollama__DefaultModel to an installed model; do not download a new model for this validation."
fi

# Upstream real-Qwen planning inputs must already exist; never regenerate them.
[ -d "$CREATIVE_DIR" ] || die "CreativeDirection artifacts missing: $CREATIVE_DIR. Run ./scripts/e2e-creative-director-qwen.sh first (or set AISTUDIO_CREATIVE_DIRECTIONS)."
[ -d "$STORY_DIR" ] || die "StoryPlan artifacts missing: $STORY_DIR. Run ./scripts/e2e-story-director-qwen.sh first (or set AISTUDIO_STORY_DIRECTIONS)."

MISSING=0
for case_dir in case-2-anime-story case-3-preschool-3d-story; do
  [ -f "$CREATIVE_DIR/$case_dir/creative-direction.json" ] || { say "  missing: creative/$case_dir/creative-direction.json"; MISSING=1; }
  [ -f "$STORY_DIR/$case_dir/story-plan.json" ] || { say "  missing: story/$case_dir/story-plan.json"; MISSING=1; }
done
[ "$MISSING" -eq 0 ] || die "One or more upstream artifacts are missing."
say "  inputs:   2/2 creative + 2/2 story present (anime-story, preschool-3d-story)"
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
dotnet "$HARNESS_DLL" \
  --creative "$CREATIVE_DIR" \
  --story "$STORY_DIR" \
  --output "$OUTPUT_DIR"
CODE=$?
set -e

exit "$CODE"
