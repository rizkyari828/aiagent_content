#!/usr/bin/env bash
#
# Grounded Script + Storyboard Real-Qwen Validation v1 — one-command runner.
#
# For each of the two grounded cases it rebuilds the grounded state deterministically
# from the saved Bible proposal + the ORIGINAL StoryPlan (fresh registries -> explicit
# register -> StoryPlanGrounder -> StoryContextBuilder), then drives the PRODUCTION
# GenerateScriptJobHandler and GenerateStoryboardJobHandler with that SAME grounded
# state and the real OllamaTextGenerator. No database, no durable job, no media, no
# upstream rerun, no model download.
#
# Exactly two cases; each case makes one real Script request plus one real Storyboard
# request (4 generations total). The real run is USER-EXECUTED. This script only
# orchestrates it.
#
# Usage:
#   scripts/e2e-grounded-content-qwen.sh
#
# Configuration uses the repository's local convention (sourced from .env):
#   Ollama__BaseUrl                 default http://127.0.0.1:11434
#   Ollama__DefaultModel            default qwen3.8:27b-q4_K_M (must be installed locally)
#   Ollama__TimeoutSeconds          default 300 (1..600) per-request timeout
#   AISTUDIO_SCRIPT_LANGUAGE        default English
#   AISTUDIO_CREATIVE_DIRECTIONS    creative-direction artifacts dir
#   AISTUDIO_STORY_DIRECTIONS       story-plan artifacts dir
#   STORY_BIBLE_ARTIFACT_DIR        story-bible artifact dir override (wins; absolute path)
#   AISTUDIO_STORY_BIBLE_DIRECTIONS story-bible artifact dir override (legacy fallback)
#   AISTUDIO_VALIDATION_OUTPUT      artifact directory override
#
# Story Bible selection: an explicit override (STORY_BIBLE_ARTIFACT_DIR, then the
# legacy AISTUDIO_STORY_BIBLE_DIRECTIONS) always wins. Otherwise the newest COMPLETE
# story-bible-qwen-* directory under .demo/artifacts is discovered automatically;
# incomplete candidates are skipped and no valid artifact is a clear failure. The
# runner never hardcodes a Story Bible timestamp.
#
# Exit codes: 0 for a completed run (even with per-case REVIEW/FAIL findings),
#             1 for a total validation failure, 2 for a runner/infrastructure failure.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HARNESS_PROJECT="$REPO_ROOT/.demo/GroundedContentQwenValidation/AIStudio.GroundedContentValidation.csproj"
HARNESS_DLL="$REPO_ROOT/.demo/GroundedContentQwenValidation/bin/Release/net10.0/AIStudio.GroundedContentValidation.dll"
DEFAULT_CREATIVE_DIR="$REPO_ROOT/.demo/artifacts/creative-director-qwen-20260921T125832Z"
DEFAULT_STORY_DIR="$REPO_ROOT/.demo/artifacts/story-director-qwen-20260921T135529Z"
BIBLE_ARTIFACT_ROOT="$REPO_ROOT/.demo/artifacts"

# shellcheck source=lib/story-bible-artifact-selection.sh
. "$REPO_ROOT/scripts/lib/story-bible-artifact-selection.sh"

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
LANGUAGE="${AISTUDIO_SCRIPT_LANGUAGE:-English}"
export Ollama__BaseUrl="$BASE_URL"
export Ollama__DefaultModel="$MODEL"
export Ollama__TimeoutSeconds="$TIMEOUT_SECONDS"
export AISTUDIO_SCRIPT_LANGUAGE="$LANGUAGE"

CREATIVE_DIR="${AISTUDIO_CREATIVE_DIRECTIONS:-$DEFAULT_CREATIVE_DIR}"
STORY_DIR="${AISTUDIO_STORY_DIRECTIONS:-$DEFAULT_STORY_DIR}"

# Resolve the Story Bible artifact: explicit override wins, else newest COMPLETE.
if [ -n "${STORY_BIBLE_ARTIFACT_DIR:-}" ]; then
  BIBLE_OVERRIDE="$STORY_BIBLE_ARTIFACT_DIR"
  BIBLE_DIR_SOURCE="explicit STORY_BIBLE_ARTIFACT_DIR"
elif [ -n "${AISTUDIO_STORY_BIBLE_DIRECTIONS:-}" ]; then
  BIBLE_OVERRIDE="$AISTUDIO_STORY_BIBLE_DIRECTIONS"
  BIBLE_DIR_SOURCE="explicit AISTUDIO_STORY_BIBLE_DIRECTIONS"
else
  BIBLE_OVERRIDE=""
  BIBLE_DIR_SOURCE="discovered newest complete"
fi

BIBLE_DIR="$(story_bible_select_artifact_dir "$BIBLE_OVERRIDE" "$BIBLE_ARTIFACT_ROOT")"

if [ -z "$BIBLE_DIR" ]; then
  die "No complete Story Bible artifact found under $BIBLE_ARTIFACT_ROOT. Expected a story-bible-qwen-* directory containing ${STORY_BIBLE_REQUIRED_CASES[*]} each with $STORY_BIBLE_PLAN_FILE. Run ./scripts/e2e-story-bible-qwen.sh first, or set STORY_BIBLE_ARTIFACT_DIR=/absolute/path."
fi

if [ ! -d "$BIBLE_DIR" ]; then
  die "Story Bible artifact does not exist: $BIBLE_DIR. Set STORY_BIBLE_ARTIFACT_DIR to an existing absolute path."
fi

if ! story_bible_artifact_is_complete "$BIBLE_DIR"; then
  die "Story Bible artifact is incomplete: $BIBLE_DIR (expected ${STORY_BIBLE_REQUIRED_CASES[*]}/$STORY_BIBLE_PLAN_FILE)."
fi

export AISTUDIO_CREATIVE_DIRECTIONS="$CREATIVE_DIR"
export AISTUDIO_STORY_DIRECTIONS="$STORY_DIR"
export AISTUDIO_STORY_BIBLE_DIRECTIONS="$BIBLE_DIR"

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUTPUT_DIR="${AISTUDIO_VALIDATION_OUTPUT:-$REPO_ROOT/.demo/artifacts/grounded-content-qwen-$STAMP}"
mkdir -p "$OUTPUT_DIR"

TAGS_FILE="$(mktemp)"
trap 'rm -f "$TAGS_FILE"' EXIT

say "Preflight"
say "  ollama:   $BASE_URL"
say "  model:    $MODEL"
say "  language: $LANGUAGE"
say "  creative: $CREATIVE_DIR"
say "  story:    $STORY_DIR"
say "  bible:    $BIBLE_DIR  [$BIBLE_DIR_SOURCE]"

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
# name:creative_case:story_case:bible_case  (the three directories use different folder names)
for mapping in \
  "anime-story:case-2-anime-story:case-2-anime-story:case-1-anime-story" \
  "preschool-3d-story:case-3-preschool-3d-story:case-3-preschool-3d-story:case-2-preschool-3d-story"; do
  IFS=':' read -r name creative_case story_case bible_case <<<"$mapping"
  [ -f "$CREATIVE_DIR/$creative_case/creative-direction.json" ] || { say "  missing: $name creative/$creative_case/creative-direction.json"; MISSING=1; }
  [ -f "$STORY_DIR/$story_case/story-plan.json" ] || { say "  missing: $name story/$story_case/story-plan.json"; MISSING=1; }
  [ -f "$BIBLE_DIR/$bible_case/story-bible-plan.json" ] || { say "  missing: $name bible/$bible_case/story-bible-plan.json"; MISSING=1; }
done
[ "$MISSING" -eq 0 ] || die "One or more upstream artifacts are missing."
say "  inputs:   2/2 creative + 2/2 story + 2/2 bible present (anime-story, preschool-3d-story)"
say "  expected: 4 real model generations (1 Script + 1 Storyboard per case)"
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
  --bible "$BIBLE_DIR" \
  --output "$OUTPUT_DIR"
CODE=$?
set -e

exit "$CODE"
