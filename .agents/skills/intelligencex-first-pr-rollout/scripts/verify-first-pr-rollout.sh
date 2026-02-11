#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<USAGE
Usage: $0 --repo <owner/name> --pr <number> [--out-dir <dir>] [--require-config] [--require-analysis-sections]

Examples:
  $0 --repo EvotecIT/IntelligenceX --pr 210 --require-config --require-analysis-sections
USAGE
}

REPO=""
PR=""
OUT_DIR="artifacts/first-pr-rollout"
REQUIRE_CONFIG=0
REQUIRE_ANALYSIS_SECTIONS=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --repo)
      REPO="${2:-}"
      shift 2
      ;;
    --pr)
      PR="${2:-}"
      shift 2
      ;;
    --out-dir)
      OUT_DIR="${2:-}"
      shift 2
      ;;
    --require-config)
      REQUIRE_CONFIG=1
      shift
      ;;
    --require-analysis-sections)
      REQUIRE_ANALYSIS_SECTIONS=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "ERROR: unknown arg: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ -z "$REPO" || -z "$PR" ]]; then
  echo "ERROR: --repo and --pr are required" >&2
  usage >&2
  exit 1
fi

if ! command -v gh >/dev/null 2>&1; then
  echo "ERROR: gh CLI is required" >&2
  exit 1
fi
if ! command -v rg >/dev/null 2>&1; then
  echo "ERROR: rg is required" >&2
  exit 1
fi

if ! gh auth status >/dev/null 2>&1; then
  echo "ERROR: gh auth status failed; login required" >&2
  exit 1
fi

ROOT="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$ROOT"

STAMP="$(date +%Y%m%d-%H%M%S)"
SAFE_REPO="${REPO//\//_}"
RUN_DIR="$OUT_DIR/${SAFE_REPO}-pr${PR}-${STAMP}"
mkdir -p "$RUN_DIR"

PR_JSON="$RUN_DIR/pr.json"
CHECKS_TXT="$RUN_DIR/checks.txt"
COMMENTS_TXT="$RUN_DIR/comments.txt"
WORKFLOW_YAML="$RUN_DIR/workflow.review-intelligencex.yml"
REVIEWER_JSON="$RUN_DIR/reviewer.json"

# Snapshot PR and checks
if ! gh pr view "$PR" --repo "$REPO" --json number,url,state,mergeable,mergeStateStatus,statusCheckRollup > "$PR_JSON"; then
  echo "ERROR: failed to fetch PR metadata" >&2
  exit 1
fi
if ! gh pr checks "$PR" --repo "$REPO" > "$CHECKS_TXT"; then
  # keep output for diagnostics even when exit code is non-zero
  true
fi
if ! gh pr view "$PR" --repo "$REPO" --json comments --jq '.comments[].body' > "$COMMENTS_TXT"; then
  echo "ERROR: failed to fetch PR comments" >&2
  exit 1
fi

# Resolve default branch
DEFAULT_BRANCH="$(gh repo view "$REPO" --json defaultBranchRef --jq '.defaultBranchRef.name')"
if [[ -z "$DEFAULT_BRANCH" || "$DEFAULT_BRANCH" == "null" ]]; then
  echo "ERROR: failed to resolve default branch" >&2
  exit 1
fi

# Fetch workflow and reviewer config from default branch
if ! gh api "repos/$REPO/contents/.github/workflows/review-intelligencex.yml?ref=$DEFAULT_BRANCH" --jq '.content' \
  | tr -d '\n' | base64 --decode > "$WORKFLOW_YAML"; then
  echo "ERROR: reviewer workflow not found on $DEFAULT_BRANCH" >&2
  exit 1
fi

gh api "repos/$REPO/contents/.intelligencex/reviewer.json?ref=$DEFAULT_BRANCH" --jq '.content' \
  | tr -d '\n' | base64 --decode > "$REVIEWER_JSON" 2>/dev/null || true

# Validate managed workflow shape (reuse bootstrap validator)
VALIDATOR="$ROOT/.agents/skills/intelligencex-reviewer-bootstrap/scripts/verify-managed-workflow.sh"
if [[ -x "$VALIDATOR" ]]; then
  "$VALIDATOR" "$WORKFLOW_YAML"
else
  if ! rg -q '# INTELLIGENCEX:BEGIN' "$WORKFLOW_YAML"; then
    echo "ERROR: workflow missing INTELLIGENCEX:BEGIN" >&2
    exit 1
  fi
  if ! rg -q '# INTELLIGENCEX:END' "$WORKFLOW_YAML"; then
    echo "ERROR: workflow missing INTELLIGENCEX:END" >&2
    exit 1
  fi
fi

if [[ "$REQUIRE_CONFIG" -eq 1 && ! -s "$REVIEWER_JSON" ]]; then
  echo "ERROR: reviewer config required but missing on default branch" >&2
  exit 1
fi

# Verify required check conclusions (allow case variants)
require_check_success() {
  local check_name="$1"
  local conclusion
  conclusion="$(gh pr view "$PR" --repo "$REPO" --json statusCheckRollup \
    --jq ".statusCheckRollup[] | select(.name == \"$check_name\") | .conclusion" | head -n1 || true)"

  if [[ -z "$conclusion" ]]; then
    echo "ERROR: required check not found: $check_name" >&2
    exit 1
  fi

  local norm
  norm="$(printf '%s' "$conclusion" | tr '[:lower:]' '[:upper:]')"
  if [[ "$norm" != "SUCCESS" ]]; then
    echo "ERROR: check '$check_name' is not SUCCESS (got: $conclusion)" >&2
    exit 1
  fi
}

require_check_success "Static Analysis Gate"
require_check_success "AI Review (Fail-Open)"
require_check_success "Ubuntu"

# Reviewer comment markers
if ! rg -q '<!-- intelligencex:summary -->' "$COMMENTS_TXT"; then
  echo "ERROR: reviewer sticky summary marker not found" >&2
  exit 1
fi
if ! rg -q 'Reviewed commit:' "$COMMENTS_TXT"; then
  echo "ERROR: reviewed commit label not found in comments" >&2
  exit 1
fi
if ! rg -q 'Diff range:' "$COMMENTS_TXT"; then
  echo "ERROR: diff range label not found in comments" >&2
  exit 1
fi

if [[ "$REQUIRE_ANALYSIS_SECTIONS" -eq 1 ]]; then
  if ! rg -q '### Static Analysis Policy' "$COMMENTS_TXT"; then
    echo "ERROR: Static Analysis Policy section not found" >&2
    exit 1
  fi
  if ! rg -q '### Static Analysis' "$COMMENTS_TXT"; then
    echo "ERROR: Static Analysis section not found" >&2
    exit 1
  fi
fi

echo "OK: first PR rollout verification passed"
echo "repo=$REPO"
echo "pr=$PR"
echo "default_branch=$DEFAULT_BRANCH"
echo "snapshot_dir=$RUN_DIR"
echo "pr_json=$PR_JSON"
echo "checks_txt=$CHECKS_TXT"
echo "comments_txt=$COMMENTS_TXT"
echo "workflow_yaml=$WORKFLOW_YAML"
if [[ -s "$REVIEWER_JSON" ]]; then
  echo "reviewer_json=$REVIEWER_JSON"
else
  echo "reviewer_json=missing"
fi
