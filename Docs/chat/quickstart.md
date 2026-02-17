---
title: IX Chat Quickstart
description: Install and run IX Chat on Windows.
---

# IX Chat Quickstart

Get IX Chat running on your Windows machine in a few minutes.

## Production Safety Notice

- This quickstart is intended for local development and evaluation workflows.
- Do not point IX Chat at production systems or production credentials until you have explicit policy controls and approvals in place.
- Use isolated test/staging environments first and keep a human approval step for any mutating tool actions.

## Prerequisites

- Windows 10 (1809+) or Windows 11
- .NET 8.0 Runtime or later
- Windows App SDK runtime (installed automatically with the app)

## Running the Console Host

The console host manages AI provider connections and tool execution:

```powershell
# Clone and build
git clone https://github.com/EvotecIT/IntelligenceX.git
cd IntelligenceX

# Run the console host
pwsh .\Build\Run-Chat.ps1 -AllowRoot C:\Support\GitHub
```

## Running the Tray App

The WinUI app provides the desktop tray experience:

```powershell
# Launch the desktop app
pwsh .\Build\Run-ChatApp.ps1 -Configuration Release
```

On first launch, IX Chat will:
1. Add a tray icon to your Windows taskbar
2. Start the console host process automatically
3. Open the chat window

## First Login

1. Click the tray icon to open the chat window
2. Open **Options -> Runtime**
3. Pick a runtime mode:
   - **Use ChatGPT Runtime** (native)
   - **Use Copilot Subscription** (`copilot-cli`)
   - **Use LM Studio Runtime** (compatible-http)
4. Complete browser sign-in when prompted (ChatGPT or Copilot subscription path)
5. For compatible-http providers, configure base URL/API key in **Show Advanced Runtime** if needed
6. Click **Refresh Models** and verify the active runtime badge

## Status Indicators

| Indicator | Meaning |
|---|---|
| Green dot | Connected and ready |
| Yellow dot | Connecting or authenticating |
| Red dot | Disconnected or error |
| Spinning | Processing a request |

## Next Steps

- [Architecture](/docs/chat/architecture/) -- Understand the dual-process design
- [Tool Packs](/docs/tools/overview/) -- Enable AI tool calling
- [Configuration](/docs/reviewer/configuration/) -- Shared configuration options
