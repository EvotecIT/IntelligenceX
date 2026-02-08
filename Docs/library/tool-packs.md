# Tool Packs (Provider-Agnostic)

IntelligenceX tool packs are **optional** libraries that implement `ITool` and can be plugged into any provider/tool-calling loop.

Core idea:
- The **stable contract** lives in the main `IntelligenceX` repo under the `IntelligenceX.Tools` namespace (`ITool`, `ToolRegistry`, schema types).
- **Tool implementations** live in separate packages (ideally separate repos) so downstream users do not carry dependencies they do not need.

## Repository split

- `EvotecIT/IntelligenceX`
  - Provider clients (`IntelligenceX.OpenAI.*`, `IntelligenceX.Copilot.*`)
  - Tool contract (`IntelligenceX.Tools` namespace)
  - Provider-specific tool-calling orchestration (for example `IntelligenceX.OpenAI.ToolCalling`)

- `EvotecIT/IntelligenceX.Tools` (tool packs)
  - `IntelligenceX.Tools.FileSystem` (cross-platform)
  - `IntelligenceX.Tools.Email` (depends on Mailozaurr/MailKit/MimeKit)
  - `IntelligenceX.Tools.System` (OS-specific helpers; keep isolated)
  - Future Windows-only packs (recommended): `IntelligenceX.Tools.ActiveDirectory`, `IntelligenceX.Tools.EventLog`, etc.

## Recommended libraries (by domain)

These are the preferred building blocks for tool packs (so behavior is consistent across tools and AI prompts):

- Email (IMAP/SMTP/Graph):
  - `Mailozaurr` (wraps MailKit/MimeKit and adds retries/ergonomics)
- Active Directory (Windows-only):
  - `ADPlayground` (preferred over raw `System.DirectoryServices` patterns)
- Windows Event Log / EVTX analysis (Windows-only):
  - `EventViewerX` / `PSEventViewer` templates and parsing helpers (AI-friendly output)
- Local persistence for tools/apps:
  - `DbaClientX` (SQLite wrapper) when you need a small embedded store

## Dependency policy (non-negotiable)

- `IntelligenceX` core packages must **not** take dependencies on tool-pack libraries.
- Each tool pack should depend only on:
  - `IntelligenceX` (the tool contract), and
  - its own runtime dependencies (MailKit, etc.).
- Windows-only capabilities must be isolated by:
  - separate package and `net8.0-windows` target (or runtime OS checks), and
  - no transitive dependency pulled into cross-platform packs.

## AOT guidance

Tool packs should *aim* to be AOT-friendly when feasible:
- Prefer `System.Text.Json` source generation where you own DTOs.
- Avoid reflection-based serialization in hot paths.
- Avoid `Assembly.LoadFrom` and dynamic code generation.

Note: Some third-party dependencies may not be fully AOT-safe; this is a per-pack tradeoff and should be documented in the pack README.

## Naming

- Tool packs: `IntelligenceX.Tools.<Domain>` (no provider name in the package/namespace).
- Provider integrations stay provider-namespaced: `IntelligenceX.OpenAI.*`, `IntelligenceX.Copilot.*`, etc.
