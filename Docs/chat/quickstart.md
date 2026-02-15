---
title: IX Chat Quickstart
description: Install and run IX Chat on Windows.
---

# IX Chat Quickstart

Get IX Chat running on your Windows machine in a few minutes.

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
2. Select your AI provider (ChatGPT or Copilot)
3. Complete the authentication flow in the browser
4. Return to IX Chat -- you are now connected

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
