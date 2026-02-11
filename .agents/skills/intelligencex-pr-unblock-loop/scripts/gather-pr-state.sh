#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <pr-number>" >&2
  exit 1
fi

PR="$1"
REPO="EvotecIT/IntelligenceX"
REPO_ROOT="$(git rev-parse --show-toplevel)"
ARTIFACT_DIR="$REPO_ROOT/artifacts/pr-$PR"
mkdir -p "$ARTIFACT_DIR"

gh pr view "$PR" --repo "$REPO" --json number,title,headRefName,baseRefName,state,mergeable,mergeStateStatus,url > "$ARTIFACT_DIR/pr.json"
gh pr checks "$PR" --repo "$REPO" > "$ARTIFACT_DIR/checks.txt" || true
gh pr view "$PR" --repo "$REPO" --comments --json comments > "$ARTIFACT_DIR/comments.json"

echo "Saved PR snapshot to: $ARTIFACT_DIR"
