---
name: intelligencex-tools-authoring
description: Use when creating or refactoring IntelligenceX tools to maximize helper reuse, reduce duplication, and keep stable tool contracts.
---

# Skill: intelligencex-tools-authoring

Use this skill when changing files in `IntelligenceX.Tools/**`, especially when adding new tools or consolidating helper logic.

## Trigger Phrases
- "new tool"
- "tooling refactor"
- "reduce duplication in tools"
- "centralize tool helpers"
- "tool contract cleanup"

## Strict Execution Order
1. Identify target package and existing base helpers.
2. Reuse shared helper paths first (`ToolBase`, `ToolQueryHelpers`, package base helpers).
3. Implement tool/refactor changes with no output contract drift.
4. Add or update tests for helper behavior and edge cases.
5. Run targeted build/tests for changed tool packages.
6. Summarize reusable pattern changes for future tools.

## Commands
- Targeted build:
  - `dotnet build IntelligenceX.Tools/IntelligenceX.Tools.ADPlayground/IntelligenceX.Tools.ADPlayground.csproj -c Release`
  - `dotnet build IntelligenceX.Tools/IntelligenceX.Tools.EventLog/IntelligenceX.Tools.EventLog.csproj -c Release`
  - `dotnet build IntelligenceX.Tools/IntelligenceX.Tools.System/IntelligenceX.Tools.System.csproj -c Release`
- Targeted tests:
  - `dotnet test IntelligenceX.Tools/IntelligenceX.Tools.Tests/IntelligenceX.Tools.Tests.csproj -c Release`
- Full tool solution gate:
  - `dotnet test IntelligenceX.Tools/IntelligenceX.Tools.sln -c Release`

## Fail-Fast Rules
- Do not add package-specific duplicate helpers when a shared helper exists.
- Do not change `error_code`/metadata/table view contracts without explicit intent.
- Keep mass normalization (for example line endings) in a dedicated cleanup PR, not mixed with behavior changes.

## References
- `InternalDocs/agent-playbooks/tool-authoring-playbook.md`

