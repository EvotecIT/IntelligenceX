#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <pr-number>" >&2
  exit 1
fi

gh pr checks "$1" --repo EvotecIT/IntelligenceX --watch --interval 10
