#!/usr/bin/env bash
set -euo pipefail

pwsh -NoLogo -NoProfile -File .github/scripts/test-chat-scenario-catalog-quality.ps1 \
  -ScenarioDir ./IntelligenceX.Chat/scenarios \
  -Filter "*-10-turn.json"
