# Repo Map

The ecosystem is intentionally split to keep dependencies optional and allow private/public sub-repos later.

## IntelligenceX (core)

Repo: `EvotecIT/IntelligenceX`

Responsibilities:
- Provider clients and transports (`IntelligenceX.OpenAI.*`, `IntelligenceX.Copilot.*`, etc.)
- Stable tool contract in the `IntelligenceX.Tools` namespace:
  - `ITool`
  - `ToolDefinition` / schema types
  - `ToolRegistry`
- Provider-specific tool-calling orchestration (for example `IntelligenceX.OpenAI.ToolCalling`)

Non-goals:
- Shipping domain-specific tool implementations (Email/AD/EventLog/etc.)

## IntelligenceX.Tools (tool packs)

Repo: `EvotecIT/IntelligenceX.Tools`

Responsibilities:
- Optional tool packs implementing `ITool`
- Owns dependencies per domain

Examples:
- `IntelligenceX.Tools.FileSystem` (cross-platform)
- `IntelligenceX.Tools.Email` (Mailozaurr/MailKit/MimeKit)
- `IntelligenceX.Tools.System` (OS/system helpers)
- Future Windows-only packs:
  - `IntelligenceX.Tools.ActiveDirectory` (ADPlayground)
  - `IntelligenceX.Tools.EventLog` (EventViewerX)

## IntelligenceX.Chat (Windows app)

Repo: `EvotecIT/IntelligenceX.Chat`

Responsibilities:
- Windows tray chat UI (WinUI 3). App name: **IntelligenceX Chat**.
- Rich output rendering (tables, markdown, code blocks)
- Settings UX for provider selection and tool-pack enablement

Non-goals:
- Tool implementations (stay in `IntelligenceX.Tools`)
- Provider-specific contracts (stay in `IntelligenceX`)
