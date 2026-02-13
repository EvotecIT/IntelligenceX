# Windows Tray Chat App (Planned)

Goal: a Windows systray app with a chat-like interface that can run tools (files, Event Log, AD, etc.) and render rich outputs (tables, code blocks, expandable tool traces).

## Current Mono-Repo Run Commands

Use repo-root `Build\` scripts as the canonical entrypoints:

```powershell
# Console host (recommended default runtime)
pwsh .\Build\Run-Chat.ps1 -AllowRoot C:\Support\GitHub

# WinUI app
pwsh .\Build\Run-ChatApp.ps1 -Configuration Release

# Service-only mode (advanced/debug)
pwsh .\Build\Run-ChatService.ps1 -AllowRoot C:\Support\GitHub
```

Recommended split:
- Core library: `EvotecIT/IntelligenceX` (public)
- Tool packs: `EvotecIT/IntelligenceX.Tools` (private until stable; publish selectively later)
- App repo: `EvotecIT/IntelligenceX.Chat` (or `EvotecIT/IntelligenceX.Desktop`) (can be public)

## UI stack recommendation (Windows)

If you want it to feel native and look premium:
- Windows App SDK + WinUI 3
- Tray integration: `H.NotifyIcon` (WinUI 3 compatible)
- Markdown rendering: WebView2-based markdown renderer (for GitHub-flavored tables) or a dedicated WinUI markdown control if it supports tables

Why:
- WinUI 3 has the best Windows-native typography/layout.
- WebView2 makes table rendering easy and consistent.

## App architecture

- One local “agent host” process:
  - `IntelligenceX.OpenAI` (or other provider) for chat
  - `ToolRegistry` populated from selected tool packs
- UI connects to host via local HTTP/Named Pipes (keep the UI thin).
- Tool execution produces:
  - human-facing summary
  - machine-readable JSON artifact (for follow-up tool calls)

## Tool packs (Windows-only examples)

Keep these out of the core repo and out of cross-platform packs:
- `IntelligenceX.Tools.ActiveDirectory` (ADPlayground)
- `IntelligenceX.Tools.EventLog` (EventViewerX / PSEventViewer)
