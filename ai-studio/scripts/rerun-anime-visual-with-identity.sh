#!/usr/bin/env bash
#
# Rerun anime scene visuals with one approved identity reference.
#
# Operator helper for the character-consistency step of Video #1. It performs
# exactly three API steps and then polls the visual job:
#   1. import the identity PNG   -> POST /api/identity-assets   (allocates a version)
#   2. approve that exact version -> POST /api/identity-assets/{assetId}/versions/{v}/approve
#   3. enqueue visual regeneration with productionRecipe + artDirection + identityReference
#      -> POST /api/content-projects/{projectId}/storyboard-jobs/{storyboardJobId}/visual-jobs
#   4. poll GET /api/jobs/{jobId} until completed/failed and print a summary.
#
# It intentionally does NOT trigger a final render or Final Video QA.
#
# ComfyUI must already be running (this script assumes the API and its providers
# are already up; it does not start or configure them).
#
# Usage:
#   ./scripts/rerun-anime-visual-with-identity.sh \
#     --project-id <guid> \
#     --storyboard-job-id <guid> \
#     --identity-png /path/to/identity.png \
#     --identity-asset-id <lowercase-id> \
#     --api http://127.0.0.1:5080
#
# Options:
#   --art-direction <text>     Override creative art direction (default: anime recipe).
#   --recipe-id <id>           Default: motion-comic.
#   --recipe-version <n>       Default: 1.
#   --poll-interval <seconds>  Default: 5.
#   --poll-timeout <seconds>   Default: 7200.
#   --reuse                    Reuse current-version scene assets instead of forcing
#                              regeneration. NOT recommended: the scene fingerprint
#                              does not include identity, so reuse skips the new
#                              identity reference entirely.
#   -h, --help                 Show this help.
#
# Requires: bash, curl, jq.

set -euo pipefail

DEFAULT_ART_DIRECTION='original cinematic anime, detailed anime background, clean expressive linework, soft cel shading, cinematic lighting, cohesive character design, deep blue night tones with warm amber highlights, not photorealistic, not 3D render, not western comic-book style'

PROJECT_ID=""
STORYBOARD_JOB_ID=""
IDENTITY_PNG=""
ASSET_ID=""
API_URL="http://127.0.0.1:5080"
ART_DIRECTION="$DEFAULT_ART_DIRECTION"
RECIPE_ID="motion-comic"
RECIPE_VERSION="1"
POLL_INTERVAL="5"
POLL_TIMEOUT="7200"
FORCE="true"

log()  { printf '[identity-visual] %s\n' "$*"; }
say()  { printf '%s\n' "$*"; }
die()  { printf '[identity-visual] ERROR: %s\n' "$*" >&2; exit 1; }

usage() {
    cat <<'EOF'
Rerun anime scene visuals with one approved identity reference.

Usage:
  ./scripts/rerun-anime-visual-with-identity.sh \
    --project-id <guid> \
    --storyboard-job-id <guid> \
    --identity-png /path/to/identity.png \
    --identity-asset-id <lowercase-id> \
    --api http://127.0.0.1:5080

Options:
  --art-direction <text>     Override creative art direction (default: anime recipe).
  --recipe-id <id>           Default: motion-comic.
  --recipe-version <n>       Default: 1.
  --poll-interval <seconds>  Default: 5.
  --poll-timeout <seconds>   Default: 7200.
  --reuse                    Reuse current-version scene assets instead of forcing
                             regeneration. NOT recommended: the scene fingerprint
                             does not include identity, so reuse skips the new
                             identity reference entirely.
  -h, --help                 Show this help.

ComfyUI must already be running. Does not trigger final render or Final QA.
Requires: bash, curl, jq.
EOF
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --project-id)        PROJECT_ID="${2:-}"; shift 2 ;;
        --storyboard-job-id) STORYBOARD_JOB_ID="${2:-}"; shift 2 ;;
        --identity-png)      IDENTITY_PNG="${2:-}"; shift 2 ;;
        --identity-asset-id) ASSET_ID="${2:-}"; shift 2 ;;
        --api)               API_URL="${2:-}"; shift 2 ;;
        --art-direction)     ART_DIRECTION="${2:-}"; shift 2 ;;
        --recipe-id)         RECIPE_ID="${2:-}"; shift 2 ;;
        --recipe-version)    RECIPE_VERSION="${2:-}"; shift 2 ;;
        --poll-interval)     POLL_INTERVAL="${2:-}"; shift 2 ;;
        --poll-timeout)      POLL_TIMEOUT="${2:-}"; shift 2 ;;
        --reuse)             FORCE="false"; shift ;;
        -h|--help)           usage; exit 0 ;;
        *) die "Unknown argument: $1 (use --help)." ;;
    esac
done

API_URL="${API_URL%/}"

[ -n "$PROJECT_ID" ]        || die "--project-id is required."
[ -n "$STORYBOARD_JOB_ID" ] || die "--storyboard-job-id is required."
[ -n "$IDENTITY_PNG" ]      || die "--identity-png is required."
[ -n "$ASSET_ID" ]          || die "--identity-asset-id is required."
[ -f "$IDENTITY_PNG" ]      || die "identity PNG not found: $IDENTITY_PNG"
[ -s "$IDENTITY_PNG" ]      || die "identity PNG is empty: $IDENTITY_PNG"

command -v curl >/dev/null || die "curl is required."
command -v jq   >/dev/null || die "jq is required."

HTTP_CODE=""
HTTP_BODY=""

# api_call <METHOD> <URL> [curl args...] -> sets HTTP_CODE/HTTP_BODY
api_call() {
    local method="$1" url="$2"; shift 2
    local tmp
    tmp="$(mktemp)"
    HTTP_CODE="$(curl -sS -o "$tmp" -w '%{http_code}' -X "$method" "$url" "$@" || true)"
    HTTP_BODY="$(cat "$tmp")"
    rm -f "$tmp"
}

say "=============================================================="
say " Rerun anime visuals with identity reference"
say "=============================================================="
say " api:        $API_URL"
say " project:    $PROJECT_ID"
say " storyboard: $STORYBOARD_JOB_ID"
say " asset id:   $ASSET_ID"
say " png:        $IDENTITY_PNG"
say " recipe:     $RECIPE_ID v$RECIPE_VERSION"
say " force:      $FORCE"
say ""

# --- 1. Import PNG identity reference ---------------------------------------
log "importing identity PNG ..."
api_call POST "$API_URL/api/identity-assets" \
    -F "assetId=$ASSET_ID" \
    -F "file=@${IDENTITY_PNG};type=image/png"

if [ "$HTTP_CODE" != "200" ]; then
    say " ! import failed (HTTP $HTTP_CODE):"
    say "$HTTP_BODY"
    die "identity import rejected."
fi

ASSET_VERSION="$(jq -r '.version // empty' <<<"$HTTP_BODY")"
[ -n "$ASSET_VERSION" ] || { say "$HTTP_BODY"; die "import response did not contain a version."; }
say " imported $ASSET_ID v$ASSET_VERSION (status=$(jq -r '.status // "?"' <<<"$HTTP_BODY"), bytes=$(jq -r '.byteSize // "?"' <<<"$HTTP_BODY"))"

# --- 2. Approve the imported version -----------------------------------------
log "approving $ASSET_ID v$ASSET_VERSION ..."
api_call POST "$API_URL/api/identity-assets/$ASSET_ID/versions/$ASSET_VERSION/approve"

if [ "$HTTP_CODE" != "200" ]; then
    say " ! approve failed (HTTP $HTTP_CODE):"
    say "$HTTP_BODY"
    die "identity approval rejected."
fi
say " approved $ASSET_ID v$ASSET_VERSION (status=$(jq -r '.status // "?"' <<<"$HTTP_BODY"))"

# --- 3. Enqueue visual regeneration with recipe + art direction + identity ---
log "enqueuing visual job ..."
PAYLOAD="$(jq -n \
    --arg rid "$RECIPE_ID" \
    --argjson rver "$RECIPE_VERSION" \
    --arg ad "$ART_DIRECTION" \
    --arg aid "$ASSET_ID" \
    --argjson aver "$ASSET_VERSION" \
    '{
        productionRecipe: { id: $rid, version: $rver },
        artDirection: $ad,
        identityReference: { assetId: $aid, version: $aver }
    }')"

api_call POST \
    "$API_URL/api/content-projects/$PROJECT_ID/storyboard-jobs/$STORYBOARD_JOB_ID/visual-jobs?force=$FORCE" \
    -H 'Content-Type: application/json' \
    -d "$PAYLOAD"

if [ "$HTTP_CODE" != "202" ]; then
    say " ! enqueue failed (HTTP $HTTP_CODE):"
    say "$HTTP_BODY"
    die "visual job enqueue rejected."
fi

JOB_ID="$(jq -r '.jobId // empty' <<<"$HTTP_BODY")"
[ -n "$JOB_ID" ] || { say "$HTTP_BODY"; die "enqueue response did not contain a jobId."; }
say " enqueued visual job $JOB_ID"

# --- 4. Poll until terminal ---------------------------------------------------
log "polling job $JOB_ID (interval ${POLL_INTERVAL}s, timeout ${POLL_TIMEOUT}s) ..."
START=$SECONDS
DEADLINE=$((SECONDS + POLL_TIMEOUT))
JOB_JSON=""

while :; do
    api_call GET "$API_URL/api/jobs/$JOB_ID"
    if [ "$HTTP_CODE" = "200" ]; then
        STATUS="$(jq -r '.status // empty' <<<"$HTTP_BODY")"
        case "$STATUS" in
            completed) JOB_JSON="$HTTP_BODY"; log "job completed in $((SECONDS - START))s"; break ;;
            failed|cancelled)
                JOB_JSON="$HTTP_BODY"
                say " ! job $STATUS: $(jq -r '.errorCode // "?"' <<<"$JOB_JSON"): $(jq -r '.errorSummary // ""' <<<"$JOB_JSON")"
                break ;;
        esac
        printf '.'
    else
        printf 'x'
    fi

    if [ "$SECONDS" -ge "$DEADLINE" ]; then
        say ""
        die "timed out after ${POLL_TIMEOUT}s waiting for job $JOB_ID (still $([ -n "${STATUS:-}" ] && echo "$STATUS" || echo unknown))."
    fi
    sleep "$POLL_INTERVAL"
done
say ""

FINAL_STATUS="$(jq -r '.status // "?"' <<<"$JOB_JSON")"

say "=============================================================="
say " RESULT"
say "=============================================================="
say " job id:       $JOB_ID"
say " status:       $FINAL_STATUS"
say " identity:     $ASSET_ID v$ASSET_VERSION (approved)"
say " recipe:       $RECIPE_ID v$RECIPE_VERSION"
say ""

if [ "$FINAL_STATUS" = "completed" ]; then
    say " sceneCount:   $(jq -r '.result.sceneCount // "?"' <<<"$JOB_JSON")"
    say " generated:    $(jq -r '.result.generatedCount // "?"' <<<"$JOB_JSON")"
    say " reused:       $(jq -r '.result.skippedCount // "?"' <<<"$JOB_JSON")"
    say ""
    say " per-scene:"
    jq -r '(.result.visuals // [])[] | "   scene \(.sceneIndex): engine=\(.engine) template=\(.template) status=\(.status) bytes=\(.byteSize)"' <<<"$JOB_JSON"
    if jq -e '(.result.routing // []) | length > 0' >/dev/null <<<"$JOB_JSON"; then
        say ""
        say " routing:"
        jq -r '(.result.routing // [])[] | "   scene \(.sceneIndex): intent=\(.intendedEngine) -> engine=\(.engine) status=\(.status)"' <<<"$JOB_JSON"
    fi
    say ""
    say " Next (manual, NOT done here): review the scene assets, then trigger"
    say " RenderVideo and Final Video QA yourself when ready."
else
    say " error code:   $(jq -r '.errorCode // "?"' <<<"$JOB_JSON")"
    say " error:        $(jq -r '.errorSummary // ""' <<<"$JOB_JSON")"
    exit 1
fi
