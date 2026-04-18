#!/usr/bin/env bash
set -euo pipefail

WORKFLOW_PATH="${1:-.github/workflows/review-intelligencex.yml}"

if [[ ! -f "$WORKFLOW_PATH" ]]; then
  echo "ERROR: workflow file not found: $WORKFLOW_PATH" >&2
  exit 1
fi

if ! rg -q '# INTELLIGENCEX:BEGIN' "$WORKFLOW_PATH"; then
  echo "ERROR: missing INTELLIGENCEX:BEGIN marker" >&2
  exit 1
fi
if ! rg -q '# INTELLIGENCEX:END' "$WORKFLOW_PATH"; then
  echo "ERROR: missing INTELLIGENCEX:END marker" >&2
  exit 1
fi
if ! rg -q '^\s*review:\s*$' "$WORKFLOW_PATH"; then
  echo "ERROR: missing review job in workflow" >&2
  exit 1
fi
if ! rg -q '^\s*uses:\s+(?:\./\.github/workflows/review-intelligencex-(?:core|reusable)\.yml|.+/\.github/workflows/review-intelligencex-(?:core|reusable)\.yml@.+)\s*$' "$WORKFLOW_PATH"; then
  echo "ERROR: missing reusable review workflow reference" >&2
  exit 1
fi
if ! rg -q '^\s*if:\s+\$\{\{.+needs-ai-review.+\}\}\s*$' "$WORKFLOW_PATH"; then
  echo "ERROR: missing fork/dependabot safety gate in managed block" >&2
  exit 1
fi
if ! rg -q '^\s*provider:\s+' "$WORKFLOW_PATH"; then
  echo "ERROR: missing provider input in managed block" >&2
  exit 1
fi
if ! rg -q '^\s*model:\s+' "$WORKFLOW_PATH"; then
  echo "ERROR: missing model input in managed block" >&2
  exit 1
fi

if rg -q '^\s*secrets:\s*inherit\s*$' "$WORKFLOW_PATH"; then
  echo "OK: workflow uses inherited secrets"
elif rg -q 'INTELLIGENCEX_AUTH_B64' "$WORKFLOW_PATH"; then
  echo "OK: workflow uses explicit secrets block"
else
  echo "ERROR: workflow has neither secrets: inherit nor explicit INTELLIGENCEX secrets" >&2
  exit 1
fi

echo "OK: managed workflow validation passed ($WORKFLOW_PATH)"
