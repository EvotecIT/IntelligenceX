#!/usr/bin/env bash
set -euo pipefail

MODE="${1:-fast}"
REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

case "$MODE" in
  fast)
    dotnet build IntelligenceX.sln -c Release
    dotnet test IntelligenceX.sln -c Release --no-build -v minimal
    ;;
  full)
    dotnet build IntelligenceX.sln -c Release
    dotnet test IntelligenceX.sln -c Release --no-build -v minimal
    dotnet ./IntelligenceX.Tests/bin/Release/net8.0/IntelligenceX.Tests.dll
    dotnet ./IntelligenceX.Tests/bin/Release/net10.0/IntelligenceX.Tests.dll
    ;;
  *)
    echo "ERROR: mode must be fast|full" >&2
    exit 1
    ;;
esac

echo "OK: onboarding validation completed ($MODE)"
