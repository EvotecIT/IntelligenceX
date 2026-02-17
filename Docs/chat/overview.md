---
title: IX Chat Overview
description: Windows desktop tray application for AI conversations with tool calling support.
---

# IX Chat

IX Chat is a Windows desktop application that lives in your system tray, providing instant access to AI-powered conversations. Built with WinUI 3 and WebView2, it combines a native desktop experience with a modern web-based chat interface.

## Key Features

- **System Tray Integration** -- Always accessible from your Windows taskbar via [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon)
- **Clear Runtime Modes** -- ChatGPT Native, Copilot Subscription (`copilot-cli`), and Compatible HTTP (LM Studio/Ollama/Azure/other)
- **Tool Calling** -- AI assistants can execute registered tool packs during conversations
- **Persistent Conversations** -- Threads are saved locally and survive app restarts
- **Zero-Trust Architecture** -- Your credentials never leave your machine

## Architecture

IX Chat uses a dual-process architecture:

| Component | Technology | Role |
|---|---|---|
| **UI Process** | WinUI 3 + WebView2 | Window management, tray icon, rendering |
| **Host Process** | Console app (`IntelligenceX.Chat.Host`) | AI provider communication, tool execution |
| **IPC** | Named pipes | Connects UI and Host processes |

## Tech Stack

- **UI Framework**: WinUI 3 (Windows App SDK)
- **Tray Icon**: H.NotifyIcon.WinUI
- **Chat Rendering**: WebView2 with embedded HTML/CSS/JS
- **Backend**: IntelligenceX .NET library
- **Storage**: Local JSON files in `%APPDATA%/IntelligenceX/`

## Getting Started

See the [Quickstart guide](/docs/chat/quickstart/) to install and run IX Chat.

## Related

- [Quickstart](/docs/chat/quickstart/) -- Install and launch IX Chat
- [Architecture](/docs/chat/architecture/) -- Deep dive into the dual-process design
- [Tool Packs](/docs/tools/overview/) -- Available tools for AI assistants
