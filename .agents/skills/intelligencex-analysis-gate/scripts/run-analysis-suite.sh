#!/usr/bin/env bash
set -euo pipefail

MODE="${1:-fast}"
find_repo_root() {
  local dir="$PWD"
  while [[ "$dir" != "/" ]]; do
    if [[ -f "$dir/IntelligenceX.sln" ]]; then
      echo "$dir"
      return 0
    fi
    dir="$(dirname "$dir")"
  done
  return 1
}

# Prefer a pure-filesystem root lookup so the script works in WSL against Windows git worktrees
# (where `.git` points at a Windows-style gitdir path that WSL git can't always resolve).
REPO_ROOT="${REPO_ROOT:-$(find_repo_root)}"
if [[ -z "${REPO_ROOT}" ]]; then
  echo "ERROR: could not locate repo root (expected to find IntelligenceX.sln)" >&2
  exit 1
fi
cd "$REPO_ROOT"

run_fast() {
  dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze validate-catalog --workspace .
  dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze run --config .intelligencex/reviewer.json --out artifacts --framework net8.0
}

case "$MODE" in
  fast)
    run_fast
    ;;
  full)
    dotnet build IntelligenceX.sln -c Release
    dotnet test IntelligenceX.sln -c Release --no-build -v minimal
    dotnet ./IntelligenceX.Tests/bin/Release/net8.0/IntelligenceX.Tests.dll
    dotnet ./IntelligenceX.Tests/bin/Release/net10.0/IntelligenceX.Tests.dll
    run_fast
    ;;
  *)
    echo "ERROR: mode must be fast|full" >&2
    exit 1
    ;;
esac

echo "OK: analysis suite completed ($MODE)"
