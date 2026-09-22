#!/usr/bin/env bash
#
# Flux.2 Klein identity-reference proof — USER-RUN only.
#
# Preflights the existing local ComfyUI/model/workflow, then uses one explicitly
# approved PNG identity reference through the real ComfyUiImageGenerationProvider
# for three fixed scenes. Outputs retain seed + pinned asset identity for human
# visual review. This runner does not create or approve a production asset.
#
# Required:
#   AISTUDIO_IDENTITY_REFERENCE_PATH      existing PNG file
#   AISTUDIO_IDENTITY_ASSET_ID            safe lowercase application id
#   AISTUDIO_IDENTITY_ASSET_VERSION       concrete positive version
#   AISTUDIO_IDENTITY_REFERENCE_APPROVAL  I_APPROVE_THIS_REFERENCE_FOR_EPHEMERAL_PROOF
#
# Optional:
#   COMFYUI_HOME                          default /home/tama/Developer/Microservices/ai/ComfyUI
#   ComfyUi__BaseUrl                      default http://127.0.0.1:8188
#   ComfyUi__TimeoutSeconds               default 600
#   ComfyUi__Width / ComfyUi__Height      default 1280 / 720
#   AISTUDIO_IDENTITY_PROOF_SEED           default 42000 (runner uses +1, +2, +3)
#   AISTUDIO_IDENTITY_PROOF_OUTPUT         output directory
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMFY_ROOT="${COMFYUI_HOME:-/home/tama/Developer/Microservices/ai/ComfyUI}"
BASE_URL="${ComfyUi__BaseUrl:-http://127.0.0.1:8188}"
HARNESS_PROJECT="$REPO_ROOT/.demo/FluxIdentityReferenceProof/AIStudio.FluxIdentityReferenceProof.csproj"
T2I_WORKFLOW="$REPO_ROOT/src/AIStudio.Infrastructure/Rendering/ComfyUI/flux2_klein_4b_distilled.json"
EDIT_WORKFLOW="$REPO_ROOT/src/AIStudio.Infrastructure/Rendering/ComfyUI/flux2_klein_4b_distilled_edit.json"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUTPUT_DIR="${AISTUDIO_IDENTITY_PROOF_OUTPUT:-$REPO_ROOT/.demo/artifacts/flux-identity-reference-$STAMP}"
APPROVAL_PHRASE="I_APPROVE_THIS_REFERENCE_FOR_EPHEMERAL_PROOF"

say() { printf '%s\n' "$*"; }
die() { printf 'ERROR: %s\n' "$*" >&2; exit 2; }

command -v curl >/dev/null || die "curl is required."
command -v dotnet >/dev/null || die "dotnet is required."

say "Preflight"
say "  ComfyUI:  $BASE_URL"
say "  install:  $COMFY_ROOT"
say "  workflow: $EDIT_WORKFLOW"
say "  output:   $OUTPUT_DIR"

[ -f "$HARNESS_PROJECT" ] || die "Proof harness is missing: $HARNESS_PROJECT"
[ -f "$T2I_WORKFLOW" ] || die "Trusted T2I workflow is missing: $T2I_WORKFLOW"
[ -f "$EDIT_WORKFLOW" ] || die "Trusted image-edit workflow is missing: $EDIT_WORKFLOW"
[ -f "$COMFY_ROOT/models/diffusion_models/flux-2-klein-4b-fp8.safetensors" ]    || die "Missing model: $COMFY_ROOT/models/diffusion_models/flux-2-klein-4b-fp8.safetensors"
[ -f "$COMFY_ROOT/models/text_encoders/qwen_3_4b_fp4_flux2.safetensors" ]    || die "Missing text encoder: $COMFY_ROOT/models/text_encoders/qwen_3_4b_fp4_flux2.safetensors"
[ -f "$COMFY_ROOT/models/vae/flux2-vae.safetensors" ]    || die "Missing VAE: $COMFY_ROOT/models/vae/flux2-vae.safetensors"

curl --fail --silent --show-error --max-time 5 "$BASE_URL/system_stats" >/dev/null    || die "ComfyUI is not reachable at $BASE_URL. Start the existing local server; do not download anything."

[ -n "${AISTUDIO_IDENTITY_REFERENCE_PATH:-}" ]    || die "No approved real reference was supplied. Set AISTUDIO_IDENTITY_REFERENCE_PATH, AISTUDIO_IDENTITY_ASSET_ID, AISTUDIO_IDENTITY_ASSET_VERSION, and AISTUDIO_IDENTITY_REFERENCE_APPROVAL=$APPROVAL_PHRASE."
[ -f "$AISTUDIO_IDENTITY_REFERENCE_PATH" ]    || die "Reference file does not exist: $AISTUDIO_IDENTITY_REFERENCE_PATH"
[ -n "${AISTUDIO_IDENTITY_ASSET_ID:-}" ] || die "AISTUDIO_IDENTITY_ASSET_ID is required."
[ -n "${AISTUDIO_IDENTITY_ASSET_VERSION:-}" ] || die "AISTUDIO_IDENTITY_ASSET_VERSION is required."
[ "${AISTUDIO_IDENTITY_REFERENCE_APPROVAL:-}" = "$APPROVAL_PHRASE" ]    || die "Explicit approval is required: export AISTUDIO_IDENTITY_REFERENCE_APPROVAL=$APPROVAL_PHRASE"

export ComfyUi__BaseUrl="$BASE_URL"
export AISTUDIO_T2I_WORKFLOW_PATH="$T2I_WORKFLOW"
export AISTUDIO_EDIT_WORKFLOW_PATH="$EDIT_WORKFLOW"
export AISTUDIO_IDENTITY_PROOF_OUTPUT="$OUTPUT_DIR"

say "Building proof harness (no generation yet)"
dotnet build "$HARNESS_PROJECT" --configuration Release --nologo

say "Running 3-scene proof with pinned asset $AISTUDIO_IDENTITY_ASSET_ID v$AISTUDIO_IDENTITY_ASSET_VERSION"
dotnet run    --project "$HARNESS_PROJECT"    --configuration Release    --no-build

say "Proof outputs: $OUTPUT_DIR"
say "Human visual review is required; this runner does not assert pixel consistency."
