#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || true)"
if [[ -z "$REPO_ROOT" ]]; then
  echo "ERROR: not inside a git repository" >&2
  exit 1
fi

cd "$REPO_ROOT"

for tool in git dotnet rg gh; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "ERROR: required tool not found: $tool" >&2
    exit 1
  fi
done

branch="$(git branch --show-current)"
if [[ "$branch" != codex/* ]]; then
  echo "ERROR: branch must start with codex/ (current: $branch)" >&2
  exit 1
fi

if [[ "${ALLOW_DIRTY:-0}" != "1" ]] && [[ -n "$(git status --porcelain)" ]]; then
  echo "ERROR: working tree is not clean (set ALLOW_DIRTY=1 to override)" >&2
  exit 1
fi

echo "OK: preflight passed"
echo "repo=$REPO_ROOT"
echo "branch=$branch"
