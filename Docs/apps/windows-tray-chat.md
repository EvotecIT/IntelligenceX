# Windows Tray Chat App

IX Chat is available today as a Windows tray application backed by the `IntelligenceX.Chat` projects in this monorepo.

## Run from source

```powershell
pwsh ./Build/Run-ChatApp.ps1 -Configuration Release
```

Optional host-only mode:

```powershell
pwsh ./Build/Run-Chat.ps1 -AllowRoot C:\Support\GitHub
```

## Architecture

- WinUI 3 desktop app with tray integration
- Host/service runtime for provider communication and tool execution
- Local-only credential and runtime model (no IntelligenceX cloud backend)

## Related docs

- [IX Chat Overview](/docs/chat/overview/)
- [IX Chat Quickstart](/docs/chat/quickstart/)
- [IX Chat Architecture](/docs/chat/architecture/)
- [Chat: Local Providers](/docs/apps/chat-local-providers/)
