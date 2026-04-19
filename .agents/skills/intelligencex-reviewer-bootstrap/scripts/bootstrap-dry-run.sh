#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<USAGE
Usage: $0 --repo <owner/name> [--mode setup|update-secret|cleanup] [--analysis enabled|disabled] [--packs <csv>] [--explicit-secrets] [--out-dir <dir>] [--token <token>] [--branch <name>]

Examples:
  $0 --repo EvotecIT/IntelligenceX --mode setup --analysis enabled --packs all-50
  $0 --repo EvotecIT/IntelligenceX --mode cleanup
USAGE
}

REPO=""
MODE="setup"
ANALYSIS="enabled"
PACKS="all-50"
EXPLICIT_SECRETS=0
OUT_DIR="artifacts/bootstrap-dry-run"
TOKEN="${INTELLIGENCEX_GITHUB_TOKEN:-${GITHUB_TOKEN:-${GH_TOKEN:-}}}"
BRANCH_NAME=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --repo)
      REPO="${2:-}"
      shift 2
      ;;
    --mode)
      MODE="${2:-}"
      shift 2
      ;;
    --analysis)
      ANALYSIS="${2:-}"
      shift 2
      ;;
    --packs)
      PACKS="${2:-}"
      shift 2
      ;;
    --explicit-secrets)
      EXPLICIT_SECRETS=1
      shift
      ;;
    --out-dir)
      OUT_DIR="${2:-}"
      shift 2
      ;;
    --token)
      TOKEN="${2:-}"
      shift 2
      ;;
    --branch)
      BRANCH_NAME="${2:-}"
      shift 2
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

if [[ -z "$REPO" ]]; then
  echo "ERROR: --repo is required" >&2
  exit 1
fi

if [[ "$MODE" != "setup" && "$MODE" != "update-secret" && "$MODE" != "cleanup" ]]; then
  echo "ERROR: --mode must be setup|update-secret|cleanup" >&2
  exit 1
fi

if [[ "$ANALYSIS" != "enabled" && "$ANALYSIS" != "disabled" ]]; then
  echo "ERROR: --analysis must be enabled|disabled" >&2
  exit 1
fi

if [[ -z "$TOKEN" ]]; then
  if command -v gh >/dev/null 2>&1; then
    TOKEN="$(gh auth token 2>/dev/null || true)"
  fi
fi

if [[ -z "$TOKEN" ]]; then
  echo "ERROR: GitHub token not found. Provide --token or set GITHUB_TOKEN/GH_TOKEN/INTELLIGENCEX_GITHUB_TOKEN." >&2
  exit 1
fi

ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT"

mkdir -p "$OUT_DIR"
STAMP="$(date +%Y%m%d-%H%M%S)"
SAFE_REPO="${REPO//\//_}"
RUN_DIR="$OUT_DIR/${SAFE_REPO}-${MODE}-${STAMP}"
mkdir -p "$RUN_DIR"
RAW_LOG="$RUN_DIR/setup-dry-run.txt"
REVIEWER_JSON="$RUN_DIR/reviewer.generated.json"
WORKFLOW_YAML="$RUN_DIR/workflow.generated.yml"

CMD=(dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 --
  setup
  --repo "$REPO"
  --github-token "$TOKEN"
  --dry-run)

if [[ -n "$BRANCH_NAME" ]]; then
  CMD+=(--branch "$BRANCH_NAME")
fi

case "$MODE" in
  setup)
    CMD+=(--with-config --skip-secret)
    if [[ "$ANALYSIS" == "enabled" ]]; then
      CMD+=(--analysis-enabled true --analysis-packs "$PACKS")
    else
      CMD+=(--analysis-enabled false)
    fi
    if [[ "$EXPLICIT_SECRETS" -eq 1 ]]; then
      CMD+=(--explicit-secrets)
    fi
    ;;
  update-secret)
    CMD+=(--update-secret)
    ;;
  cleanup)
    CMD+=(--cleanup --keep-secret)
    ;;
esac

"${CMD[@]}" | tee "$RAW_LOG"

awk '
  BEGIN { in_block=0 }
  /^--- \.intelligencex\/reviewer\.json ---$/ { in_block=1; next }
  /^--- / && in_block==1 { in_block=0 }
  in_block==1 { print }
' "$RAW_LOG" > "$REVIEWER_JSON"

awk '
  BEGIN { in_block=0 }
  /^--- \.github\/workflows\/review-intelligencex\.yml ---$/ { in_block=1; next }
  /^--- / && in_block==1 { in_block=0 }
  in_block==1 { print }
' "$RAW_LOG" > "$WORKFLOW_YAML"

if [[ "$MODE" == "setup" ]]; then
  if [[ ! -s "$REVIEWER_JSON" ]]; then
    echo "ERROR: setup mode expected reviewer config block in dry-run output." >&2
    exit 1
  fi
  if ! rg -q '"review"\s*:\s*\{' "$REVIEWER_JSON"; then
    echo "ERROR: generated reviewer config missing review block." >&2
    exit 1
  fi
  if [[ "$ANALYSIS" == "enabled" ]] && ! rg -q '"analysis"\s*:\s*\{' "$REVIEWER_JSON"; then
    echo "ERROR: analysis requested but generated reviewer config has no analysis block." >&2
    exit 1
  fi
fi

if [[ "$MODE" != "update-secret" ]]; then
  if [[ ! -s "$WORKFLOW_YAML" ]]; then
    echo "ERROR: expected workflow block in dry-run output." >&2
    exit 1
  fi
  if ! rg -q '# INTELLIGENCEX:BEGIN' "$WORKFLOW_YAML"; then
    echo "ERROR: generated workflow missing INTELLIGENCEX:BEGIN marker." >&2
    exit 1
  fi
  if ! rg -q '# INTELLIGENCEX:END' "$WORKFLOW_YAML"; then
    echo "ERROR: generated workflow missing INTELLIGENCEX:END marker." >&2
    exit 1
  fi
  if ! rg -q '^\s*uses:\s+(?:\./\.github/workflows/review-intelligencex-(?:core|reusable)\.yml|.+/\.github/workflows/review-intelligencex-(?:core|reusable)\.yml@.+)\s*$' "$WORKFLOW_YAML"; then
    echo "ERROR: generated workflow missing reusable review workflow reference." >&2
    exit 1
  fi
  if ! rg -q '^\s*if:\s+\$\{\{.+needs-ai-review.+\}\}\s*$' "$WORKFLOW_YAML"; then
    echo "ERROR: generated workflow missing fork/dependabot safety gate." >&2
    exit 1
  fi
fi

echo "OK: bootstrap dry-run checks passed"
echo "run_dir=$RUN_DIR"
echo "raw_log=$RAW_LOG"
[[ -s "$REVIEWER_JSON" ]] && echo "reviewer_json=$REVIEWER_JSON"
[[ -s "$WORKFLOW_YAML" ]] && echo "workflow_yaml=$WORKFLOW_YAML"
