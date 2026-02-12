# Architecture

## High-level

The app should be split into two layers:

- **UI (tray app)**
  - WinUI 3 shell, chat UX, settings, notification/tray
  - renders assistant output (markdown, tables, code blocks)
  - renders tool call traces (collapsible)

- **Host (agent runtime)**
  - owns provider client (OpenAI/Copilot/other)
  - owns `ToolRegistry` and tool execution
  - returns:
    - machine-readable tool outputs
    - human-readable summaries

This makes the runtime reusable (future CLI, service mode) and keeps the UI thin.

## Current (Implemented)

- `IntelligenceX.Chat.Host`: minimal REPL host (dev harness).
- `IntelligenceX.Chat.Service`: named-pipe server that owns the provider client and tool execution.
- `IntelligenceX.Chat.Abstractions`: typed NDJSON protocol (AOT-friendly via `System.Text.Json` source generation).

Protocol details: `Docs/service-protocol.md`

## Contracts

Use the `IntelligenceX.Tools` contract from `EvotecIT/IntelligenceX`:
- tools are described by `ToolDefinition` (JSON schema)
- tools execute via `ITool.InvokeAsync(...)`

The host process should expose a stable local API (HTTP/Named Pipes) with:
- start conversation / send message
- stream deltas
- tool call events + tool output events
- structured “render hints” for tables/code/diagrams

Tool output contract (UI-facing): `Docs/tool-output-contract.md`

## Tool packs

Tool packs are loaded as optional dependencies (user chooses which packs to enable).
Windows-only packs must be separate packages (do not contaminate cross-platform packs).
