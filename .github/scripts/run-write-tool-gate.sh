#!/usr/bin/env bash
set -euo pipefail

dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze run --workspace . --config .intelligencex/reviewer.json --out artifacts --pack intelligencex-maintainability-default --strict true
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj --framework net8.0 -- analyze gate --workspace . --config .intelligencex/reviewer.json
