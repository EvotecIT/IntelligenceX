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

Startup profiling (phase timing from `StartupLog`):

```powershell
pwsh .\scripts\profile-chat-startup.ps1 -Runs 5 -OutFile .\artifacts\chat-startup-profile.json
```

```bash
./scripts/profile-chat-startup.sh -Runs 5 -OutFile ./artifacts/chat-startup-profile.json
```

Add `-PostStartupGraceSeconds 2` if you want to include deferred post-startup warmup phases in the report.
Add `-ArchiveLogsDirectory .\artifacts\chat-startup-logs` to retain each run's raw startup log for hotspot/outlier investigation.
The report includes startup WebView metrics (`startup_webview_budget_ms`, `startup_webview_ms`, `ensure_webview_ms`, `webview_env_prewarm_ms`) plus budget/defer markers to track fail-open behavior.
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
5. If you see **Applying Runtime...**, wait for restart and model refresh to complete before clicking runtime buttons again.
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
- WinUI app state (profiles, chats, UI options): `%LOCALAPPDATA%\\IntelligenceX.Chat\\app-state.db`
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
