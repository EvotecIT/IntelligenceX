#!/usr/bin/env bash
set -euo pipefail

WORKFLOW_PATH="${1:-.github/workflows/review-intelligencex.yml}"
CLI_DLL="IntelligenceX.Cli/bin/Release/net8.0/IntelligenceX.Cli.dll"

if [[ -f "$CLI_DLL" ]]; then
  dotnet "$CLI_DLL" ci verify-managed-workflow --workflow "$WORKFLOW_PATH"
else
  dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -c Release -f net8.0 --no-restore -- \
    ci verify-managed-workflow --workflow "$WORKFLOW_PATH"
fi
