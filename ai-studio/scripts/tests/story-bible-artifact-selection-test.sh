#!/usr/bin/env bash
#
# Deterministic tests for Story Bible artifact selection. No real .demo/artifacts,
# no model, and no network are required: everything runs in temporary directories.
#
# Covers:
#   1. explicit STORY_BIBLE_ARTIFACT_DIR override wins
#   2. newest COMPLETE artifact is selected without an override
#   3. newest incomplete artifact is skipped in favor of newest complete
#   4. no valid artifact produces no selection (caller fails clearly)
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
# shellcheck source=../lib/story-bible-artifact-selection.sh
. "$REPO_ROOT/scripts/lib/story-bible-artifact-selection.sh"

TMP_ROOT="$(mktemp -d)"
trap 'rm -rf "$TMP_ROOT"' EXIT

failures=0

check() {
  local description="$1"
  shift
  if "$@"; then
    printf '  [ok] %s\n' "$description"
  else
    printf '  [FAILED] %s\n' "$description"
    failures=$((failures + 1))
  fi
}

is_incomplete() {
  ! story_bible_artifact_is_complete "$1"
}

# make_artifact ROOT NAME [CASE...]
# Creates $ROOT/$NAME with a story-bible-plan.json for each given case.
make_artifact() {
  local root="$1"
  local name="$2"
  shift 2

  local dir="$root/$name"
  local case_name
  for case_name in "$@"; do
    mkdir -p "$dir/$case_name"
    printf '{}\n' > "$dir/$case_name/$STORY_BIBLE_PLAN_FILE"
  done
}

OLD_NAME="story-bible-qwen-20260921T161017Z"
NEW_NAME="story-bible-qwen-20260922T003757Z"

# --- 1. explicit override wins even when a newer complete artifact exists -----
ROOT1="$TMP_ROOT/override"
mkdir -p "$ROOT1"
make_artifact "$ROOT1" "$OLD_NAME" "${STORY_BIBLE_REQUIRED_CASES[@]}"
make_artifact "$ROOT1" "$NEW_NAME" "${STORY_BIBLE_REQUIRED_CASES[@]}"

check "explicit override wins over a newer complete artifact" \
  test "$(story_bible_select_artifact_dir "$ROOT1/$OLD_NAME" "$ROOT1")" = "$ROOT1/$OLD_NAME"

# --- 2. without override, newest complete artifact is selected ----------------
check "newest complete artifact is selected without an override" \
  test "$(story_bible_select_artifact_dir "" "$ROOT1")" = "$ROOT1/$NEW_NAME"

# --- 3. newest incomplete candidate is skipped --------------------------------
ROOT3="$TMP_ROOT/incomplete"
mkdir -p "$ROOT3"
make_artifact "$ROOT3" "$OLD_NAME" "${STORY_BIBLE_REQUIRED_CASES[@]}"
# Newest directory missing the preschool case: incomplete.
make_artifact "$ROOT3" "$NEW_NAME" "${STORY_BIBLE_REQUIRED_CASES[0]}"

check "newest candidate is detected as incomplete" \
  is_incomplete "$ROOT3/$NEW_NAME"
check "newest incomplete artifact is skipped for the newest complete one" \
  test "$(story_bible_select_artifact_dir "" "$ROOT3")" = "$ROOT3/$OLD_NAME"

# --- 4. no valid artifact selects nothing ------------------------------------
ROOT4="$TMP_ROOT/none"
mkdir -p "$ROOT4"
# Only an incomplete candidate present.
make_artifact "$ROOT4" "$NEW_NAME" "${STORY_BIBLE_REQUIRED_CASES[0]}"

check "empty root selects nothing" \
  test -z "$(story_bible_select_artifact_dir "" "$ROOT4")"
check "missing root selects nothing" \
  test -z "$(story_bible_select_artifact_dir "" "$TMP_ROOT/does-not-exist")"
check "incomplete-only root selects nothing" \
  test -z "$(story_bible_select_artifact_dir "" "$ROOT4")"

# A complete artifact is reported complete.
check "complete artifact is detected as complete" \
  story_bible_artifact_is_complete "$ROOT1/$NEW_NAME"

if [ "$failures" -eq 0 ]; then
  printf 'Story Bible artifact selection tests PASS.\n'
  exit 0
fi

printf 'Story Bible artifact selection tests FAIL (%d failed).\n' "$failures" >&2
exit 1
