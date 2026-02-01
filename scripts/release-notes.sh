#!/usr/bin/env bash
set -euo pipefail

dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -- release notes --update-changelog
