# IntelligenceX.Tools

Tool packs for IntelligenceX tool calling.

Design goals:
- Keep the main `IntelligenceX` library lean: tool packs (and their dependencies) live here.
- Provider-agnostic: packs do not depend on `IntelligenceX.OpenAI.*` or any provider-specific code.
- Pack what you need: consumers reference only the packs they want.

## Related repos

- `EvotecIT/IntelligenceX`: core library + tool contract (`IntelligenceX.Tools` namespace).
  - Tool pack model and rules: https://github.com/EvotecIT/IntelligenceX/blob/master/Docs/library/tool-packs.md
- `EvotecIT/IntelligenceX.Chat`: planned Windows tray chat app (UI host for providers + tool packs).

## Local development

This repo is engine-first during development and prefers local `ProjectReference` to sibling checkouts
so tool packs stay thin wrappers around the real engines.

- Bootstrap script: `scripts/bootstrap-dev.ps1`
- Details: `Docs/DevBootstrap.md`

## Tool packs in this repo

- `IntelligenceX.Tools.FileSystem`
  - Cross-platform file querying primitives intended for AI consumption
  - Includes `fs_pack_info` guidance tool
  - Dependencies: .NET only
- `IntelligenceX.Tools.Email`
  - IMAP search/get + SMTP send helpers
  - Includes `email_pack_info` guidance tool
  - Dependencies: `Mailozaurr` (wraps MailKit/MimeKit)
- `IntelligenceX.Tools.System`
  - OS/system helpers (keep OS-specific things isolated)
  - Includes `system_pack_info` guidance tool
- `IntelligenceX.Tools.PowerShell`
  - Dedicated IX.PowerShell runtime pack for `powershell.exe` / `pwsh` execution (opt-in, dangerous)
  - Includes `powershell_pack_info`, `powershell_environment_discover`, `powershell_hosts`, and `powershell_run`
  - `powershell_run` uses explicit `intent` (`read_only`/`read_write`) with policy-gated write controls
  - Engine-first via `ComputerX.PowerShellRuntime`
- `IntelligenceX.Tools.TestimoX`
  - Native TestimoX diagnostics pack for rule discovery and focused rule execution
  - Includes `testimox_pack_info`, `testimox_rules_list`, and `testimox_rules_run`
  - Engine-first via `TestimoX.Execution.TestimoRunner`
- `IntelligenceX.Tools.ReviewerSetup`
  - Reviewer onboarding path contract and command templates for bot orchestration
  - Includes `reviewer_setup_pack_info`
  - Guidance-first pack used by hosts to align CLI/Web/Bot onboarding flows
- `IntelligenceX.Tools.EventLog`
  - EVTX parsing + Windows event log querying (engine-first via `EventViewerX`)
  - Includes `eventlog_pack_info` guidance tool
- `IntelligenceX.Tools.ActiveDirectory`
  - Engine-first via `ADPlayground` (in `TestimoX` monorepo)
  - Includes `ad_pack_info` guidance tool

## Model-facing contract

- Tools are thin wrappers over engine outputs.
- Raw engine payloads are preserved for reasoning/correlation.
- Optional projection args (`columns`, `sort_by`, `sort_direction`, `top`) are view-only.
- Projection columns can be curated per tool or auto-derived from typed engine row models.
- Render-oriented projected rows are emitted in `*_view` fields.
- For pack-level planning, call the corresponding `*_pack_info` tool first.
- Use `capabilities` + `recommended_flow_steps` for planning strategy.
- Use `tool_catalog` for runtime-accurate tool descriptions, argument hints, required-argument hints, and structured usage traits.

## Adding a new pack

See `CONTRIBUTING.md` for:
- naming conventions
- dependency rules
- Windows-only guidance (`net8.0-windows`/OS checks)

## Tool Output Contract

Envelope + UI render hints + markdown guidance live in `Docs/ToolOutputContract.md`.
File-by-file modernization status lives in `Docs/TOOLS_FILE_BY_FILE_STATUS.md`.

## Build

```powershell
dotnet build IntelligenceX.Tools.sln -c Release
```
