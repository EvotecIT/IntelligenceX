---
title: Tool Packs
description: Understand IntelligenceX tool packs, their monorepo layout, packaging model, and how provider-agnostic tool contracts stay stable.
---

# Tool Packs (Provider-Agnostic)

IntelligenceX tool packs are **optional** libraries that implement `ITool` and can be plugged into any provider/tool-calling loop.

Core idea:
- The **stable contract** lives in this monorepo under the `IntelligenceX.Tools` namespace (`ITool`, `ToolRegistry`, schema types).
- Tool-pack implementations are versioned in this monorepo under `IntelligenceX.Tools/` and published as focused packages so users only install what they need.

## Monorepo layout

- `IntelligenceX/`: provider clients and tool-calling orchestration
- `IntelligenceX.Tools/`: tool pack implementations
  - `IntelligenceX.Tools.FileSystem` (cross-platform)
  - `IntelligenceX.Tools.Email` (depends on Mailozaurr/MailKit/MimeKit)
  - `IntelligenceX.Tools.EventLog` (Windows)
  - `IntelligenceX.Tools.PowerShell` (Windows/PowerShell hosts)

Note:
- Some private/internal packs may still be maintained separately, but the public source of truth for OSS tool packs is this monorepo.
- Closed-source/private packs are IX Chat private/licensed by default. Using those packs in external custom hosts requires a separate license.

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
