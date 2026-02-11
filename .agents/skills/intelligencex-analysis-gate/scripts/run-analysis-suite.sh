#!/usr/bin/env bash
set -euo pipefail

MODE="${1:-fast}"
REPO_ROOT="$(git rev-parse --show-toplevel)"
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
