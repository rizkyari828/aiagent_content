#!/usr/bin/env bash
#
# Story Bible artifact selection for the grounded-content real-Qwen runner.
#
# Extracted so the selection logic is deterministic and testable against temporary
# directories; it never requires the real (gitignored) .demo/artifacts to exist.
#
# Selection rules:
#   1. An explicit override always wins.
#   2. Otherwise the newest COMPLETE story-bible-qwen-* directory is selected.
#   3. Incomplete candidates are skipped, never silently used.
#   4. No valid candidate prints nothing so the caller can fail clearly.
# shellcheck shell=bash

# Per-case bible plan directories the grounded harness reads. They must match
# CaseCatalog in .demo/GroundedContentQwenValidation/Program.cs.
STORY_BIBLE_REQUIRED_CASES=(
  "case-1-anime-story"
  "case-2-preschool-3d-story"
)
STORY_BIBLE_PLAN_FILE="story-bible-plan.json"

# story_bible_artifact_is_complete DIR
# Returns 0 when DIR contains a plan file for every required case.
story_bible_artifact_is_complete() {
  local dir="${1:-}"

  if [ -z "$dir" ] || [ ! -d "$dir" ]; then
    return 1
  fi

  local case_name
  for case_name in "${STORY_BIBLE_REQUIRED_CASES[@]}"; do
    if [ ! -f "$dir/$case_name/$STORY_BIBLE_PLAN_FILE" ]; then
      return 1
    fi
  done

  return 0
}

# story_bible_find_latest_complete ROOT
# Prints the newest complete story-bible-qwen-* directory under ROOT (newest first
# by timestamped directory name). Prints nothing when none is complete.
story_bible_find_latest_complete() {
  local root="${1:-}"

  if [ -z "$root" ] || [ ! -d "$root" ]; then
    return 0
  fi

  local candidate
  while IFS= read -r candidate; do
    if story_bible_artifact_is_complete "$candidate"; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done < <(find "$root" -maxdepth 1 -mindepth 1 -type d -name 'story-bible-qwen-*' | LC_ALL=C sort -r)

  return 0
}

# story_bible_select_artifact_dir OVERRIDE ROOT
# Explicit OVERRIDE wins; otherwise the newest COMPLETE artifact under ROOT is
# selected. Prints the selected directory, or nothing when no valid artifact exists.
story_bible_select_artifact_dir() {
  local override="${1:-}"
  local root="${2:-}"

  if [ -n "$override" ]; then
    printf '%s\n' "$override"
    return 0
  fi

  story_bible_find_latest_complete "$root"
}
