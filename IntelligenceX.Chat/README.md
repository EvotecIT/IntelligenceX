# IntelligenceX.Chat

Windows tray chat application for IntelligenceX.

App name (planned): **IntelligenceX Chat**.

Primary goal: a **premium Windows systray** chat UI that can run local tools (files, Event Log, Active Directory, etc.), display rich outputs (tables, code blocks), and iterate with an LLM safely.

Status: active migration in mono-repo (Host + Service + WinUI App in progress).

## Runtime Entry Points

Use scripts from repo root `Build\` as the canonical entrypoint surface:

- `Build\Run-Chat.ps1` - Console host (recommended default runtime)
- `Build\Run-ChatApp.ps1` - WinUI app (desktop UI)
- `Build\Run-ChatService.ps1` - Service-only mode (advanced/debug)

### Run

Console host (recommended):

```powershell
pwsh .\Build\Run-Chat.ps1 -AllowRoot C:\Support\GitHub
```

WinUI app:

```powershell
pwsh .\Build\Run-ChatApp.ps1 -Configuration Release
```

Local repeatable scenario run (non-interactive, same real host/tools runtime):

```powershell
pwsh .\Build\Run-Chat.ps1 `
  -ScenarioFile .\IntelligenceX.Chat\scenarios\ad-reboot-local-10-turn.json `
  -ScenarioOutput .\artifacts\chat-scenarios
```

Scenario file formats:
- JSON object with `name` + `turns` (each turn can be a string or object with `user`/`name` and optional quality gates):
  - `assert_contains` (string or array)
  - `assert_not_contains` (string or array)
  - `assert_matches_regex` (string or array; regex patterns that must match assistant output)
  - `assert_no_questions` (boolean; fails when assistant output contains question markers such as `?`/`？`)
  - `min_tool_calls` (integer >= 0)
  - `min_tool_rounds` (integer >= 0)
  - `require_tools` (string or array; all listed tool names must be called; supports `*` and `?` wildcards)
  - `require_any_tools` (string or array; at least one listed tool name must be called; supports `*` and `?` wildcards)
  - `forbid_tools` (string or array; listed tool names must not be called; supports `*` and `?` wildcards)
  - `assert_tool_output_contains` (string or array; expected evidence in tool output payloads)
  - `assert_tool_output_not_contains` (string or array; disallowed content in tool output payloads)
  - `assert_no_tool_errors` (boolean; when true, fails turn if any tool output envelope has `ok=false`)
  - `forbid_tool_error_codes` (string or array; disallow specific tool `error_code` values; supports `*` and `?` wildcards)
- Plain text where each non-empty line is a user turn (`#` and `//` lines are ignored).

The host writes a markdown run report under `artifacts/chat-scenarios` by default (or your `-ScenarioOutput` path).

Included AD scenario seeds:
- `IntelligenceX.Chat/scenarios/ad-reboot-local-10-turn.json`
- `IntelligenceX.Chat/scenarios/ad-replication-health-10-turn.json`
- `IntelligenceX.Chat/scenarios/ad-identity-correlation-przemyslaw-10-turn.json`
- `IntelligenceX.Chat/scenarios/ad-ldap-adws-health-10-turn.json`
- `IntelligenceX.Chat/scenarios/ad-user-last-logon-przemyslaw-10-turn.json`

Batch run all built-in AD scenarios locally:

```powershell
pwsh .\Build\Run-ChatScenarioSuite.ps1 `
  -ScenarioDir .\IntelligenceX.Chat\scenarios `
  -Filter "ad-*-10-turn.json" `
  -OutDir .\artifacts\chat-scenarios
```

Optional flags:
- `-NoBuild` to skip restore/build in repeated local runs.
- `-StopOnFailure` to stop on first failed scenario.
- `-ContinueOnError:$false` to fail a scenario immediately when a turn fails.

Startup profiling (phase timing from `StartupLog`):

```powershell
pwsh .\scripts\profile-chat-startup.ps1 -Runs 5 -OutFile .\artifacts\chat-startup-profile.json
```

```bash
./scripts/profile-chat-startup.sh -Runs 5 -OutFile ./artifacts/chat-startup-profile.json
```

Startup profiling matrix (native + simulated hardware tiers):

```powershell
pwsh .\scripts\profile-chat-startup-matrix.ps1 -Runs 4 -OutFile .\artifacts\chat-startup-profile-matrix\matrix-report.json
```

```bash
./scripts/profile-chat-startup-matrix.sh -Runs 4 -OutFile ./artifacts/chat-startup-profile-matrix/matrix-report.json
```

Add `-PostStartupGraceSeconds 2` if you want to include deferred post-startup warmup phases in the report.
Add `-ArchiveLogsDirectory .\artifacts\chat-startup-logs` to retain each run's raw startup log for hotspot/outlier investigation.
Add `-SimulateSlowHardware -SimulatedSlowHardwareMaxLogicalCores 2 -SimulatedSlowHardwarePriorityClass BelowNormal` to emulate lower-end startup conditions on faster machines.
The report includes startup WebView metrics (`startup_webview_budget_ms`, `startup_webview_ms`, `ensure_webview_ms`, `webview_env_prewarm_ms`) plus budget/defer markers to track fail-open behavior.
The report also includes startup connect-attempt diagnostics (`startup_connect_attempts`, outlier/guardrail/timeout counters, and summary aggregates) so slow-start spikes can be analyzed without manual log parsing.
The matrix report includes per-tier deltas versus native baseline so regressions can be compared consistently across high-end and lower-end hardware profiles.
The JSON report now includes `schema_version` (`chat-startup-profile-v2`) and a `simulation` block. Consumers should use `schema_version` for branching and ignore unknown additive fields for forward compatibility.
Adaptive startup WebView budget cache is persisted at `%LOCALAPPDATA%\IntelligenceX.Chat\startup-webview-budget-cache-v1.json` (best-effort, stability/cooldown aware so slower hardware stays conservative).

Markdown renderer dependency resolution is automatic:
- Dev: if sibling local source exists (`..\OfficeIMO`), Chat builds against that local project.
- Fallback: if local source is not present (CI/clean OSS checkout), Chat uses the NuGet package.

`Run-ChatApp.ps1` launches only the app. The local runtime service is auto-started and auto-restarted by the app.

### WinUI Local Model Quick Setup

Inside the app:

1. Open **Options -> Runtime**.
2. Click one of the runtime actions:
   - **Use ChatGPT Runtime**
   - **Use LM Studio Runtime**
   - **Use Copilot Subscription**
3. Wait for model discovery to populate and choose a discovered model if needed.
4. Use **Show Advanced Runtime** only when you need custom transport/base URL/API key/manual model options (LM Studio/Ollama/Azure/other compatible endpoints).
5. If you see **Applying Runtime...**, wait for runtime apply + model refresh to complete before clicking runtime buttons again (runtime switching is live apply/reconnect, no auto-restart).
6. Use **Refresh Models** to force model re-discovery after runtime changes.

Troubleshooting:
- `Couldn't connect to local runtime after startup: Timed out waiting for service pipe.` usually means the selected runtime endpoint is not reachable yet.
- For LM Studio, ensure Local Server is enabled and reachable at `http://127.0.0.1:1234/v1` (or set your custom endpoint in **Advanced Runtime**).

Reference: `Docs/apps/chat-local-providers.md`

Service-only mode (advanced):

```powershell
pwsh .\Build\Run-ChatService.ps1 -AllowRoot C:\Support\GitHub
```

ChatGPT login is cached under `%USERPROFILE%\\.intelligencex\\auth.json` by default, so after the first login the host should show:

`ChatGPT login: using cached token.`

## Runtime State And Storage

By default, Host + Service + WinUI app share auth and use separate local state stores:

- Auth token cache (shared): `%USERPROFILE%\\.intelligencex\\auth.json`
  - Override: `INTELLIGENCEX_AUTH_PATH`
  - Account switching in Chat no longer deletes this store; it clears only runtime account pinning so existing accounts stay reusable without full reauth.
- WinUI app state (profiles, chats, UI options): `%LOCALAPPDATA%\\IntelligenceX.Chat\\app-state.db`
  - Native account slot count defaults to `3`; override with `IXCHAT_NATIVE_ACCOUNT_SLOTS` (range `1..32`) when you want more per-profile slots.
- Service profile state (service CLI profiles): `%LOCALAPPDATA%\\IntelligenceX.Chat\\state.db`
- Runtime staging (temporary service runtime copy): `%TEMP%\\IntelligenceX.Chat\\service-runtime\\<guid>`
- IPC channel: named pipe `intelligencex.chat`

Status-chip behavior is connection/auth based:

- `Starting...`: app is bringing up or reconnecting local runtime
- `Runtime unavailable`: local runtime failed to start or connect
- `Sign in to continue`: local runtime is reachable, auth cache is missing/invalid
- `Ready`: local runtime is reachable and authenticated

### Compatibility Wrappers

`IntelligenceX.Chat\run-host.ps1`, `IntelligenceX.Chat\run-app.ps1`, and `IntelligenceX.Chat\run-service.ps1` remain available and now delegate to `Build\` scripts.

## Chat Service

This repo also includes a minimal local execution service:

- `IntelligenceX.Chat.Abstractions` (typed NDJSON protocol + `System.Text.Json` source generation, AOT-friendly)
- `IntelligenceX.Chat.Service` (named-pipe server, OpenAI native provider, tool execution)

Direct run (named pipe; default pipe name is `intelligencex.chat`):

```powershell
dotnet run --project .\\IntelligenceX.Chat.Service --framework net10.0-windows -- --allow-root C:\\Support\\GitHub
```

Protocol notes: `Docs/service-protocol.md`

## Repository Map

This repo is the UI app. It intentionally stays separate from tool packs and provider implementations.

- `EvotecIT/IntelligenceX` (core)
  - provider clients (`IntelligenceX.OpenAI.*`, `IntelligenceX.Copilot.*`)
  - tool contract (`IntelligenceX.Tools` namespace: `ITool`, `ToolRegistry`, schema types)
- `EvotecIT/IntelligenceX.Tools` (tool packs)
  - optional tool implementations + dependencies (Mailozaurr, etc.)
- `EvotecIT/IntelligenceX.Chat` (this repo)
  - Windows tray app + UX
  - loads tool packs optionally

## Architecture (Proposed)

Split UI from execution:
- UI process: WinUI 3 app (Windows App SDK), tray icon integration, rendering (markdown/tables), settings UI.
- Host process: local “agent host” that:
  - runs the provider client
  - owns the tool registry
  - executes tools and returns structured results

Reason: makes tool execution reusable outside the UI and keeps the UI thin.

## Tech Stack (Proposed)

- WinUI 3 (Windows App SDK)
- Tray: `H.NotifyIcon`
- Rich rendering: WebView2 for Markdown tables + code blocks

## Docs

Start here: `Docs/index.md`
