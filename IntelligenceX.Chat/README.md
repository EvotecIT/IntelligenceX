# IntelligenceX.Chat

Windows tray chat application for IntelligenceX.

App name (planned): **IntelligenceX Chat**.

Primary goal: a **premium Windows systray** chat UI that can run local tools (files, Event Log, Active Directory, etc.), display rich outputs (tables, code blocks), and iterate with an LLM safely.

Status: planning + docs scaffold (no app implementation yet).

## Starter Host

This repo includes a small buildable starter host:

- `IntelligenceX.Chat.sln`
- `IntelligenceX.Chat.Host` (console app, `net10.0` + `net10.0-windows`)

This is intentionally minimal and exists to:
- give agents a compile-clean entry point
- later host providers + tool packs outside the UI process

### Run

Because `IntelligenceX.Chat.Host` targets multiple frameworks, specify the framework when running:

```powershell
dotnet run --project .\\IntelligenceX.Chat.Host --framework net10.0-windows -- --allow-root C:\\Support\\GitHub --parallel-tools --echo-tool-outputs
```

ChatGPT login is cached under `%USERPROFILE%\\.intelligencex\\auth.json` by default, so after the first login the host should show:

`ChatGPT login: using cached token.`

## Chat Service

This repo also includes a minimal local execution service:

- `IntelligenceX.Chat.Abstractions` (typed NDJSON protocol + `System.Text.Json` source generation, AOT-friendly)
- `IntelligenceX.Chat.Service` (named-pipe server, OpenAI native provider, tool execution)

Run (named pipe; default pipe name is `intelligencex.chat`):

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
