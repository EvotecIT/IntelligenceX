---
title: IX Tools Overview
description: Real IntelligenceX tool packs, source model, and how they are loaded in IX Chat and .NET.
---

# IX Tools

IX Tools are tool packs used by IntelligenceX hosts (especially IX Chat) to expose local capabilities to AI models.

## Source Model

IntelligenceX.Chat classifies packs with a runtime `sourceKind`:

| Source kind | Meaning |
|---|---|
| `builtin` | Pack is part of the standard bootstrap path. |
| `open_source` | Pack is loaded from external open-source plugin assemblies. |
| `closed_source` | Pack exists but may come from private/internal assemblies not present in OSS checkouts. |

## Tool Pack Loading Flow

```mermaid
flowchart LR
  classDef host fill:#BAE6FD,stroke:#0369A1,color:#082F49,stroke-width:2px;
  classDef pack fill:#DDD6FE,stroke:#5B21B6,color:#2E1065,stroke-width:2px;
  classDef runtime fill:#A7F3D0,stroke:#047857,color:#052E2B,stroke-width:2px;

  A["IX Chat host startup"] --> B["ToolPackBootstrap"]
  B --> C["Discover assemblies"]
  C --> D["Register descriptors and tools"]
  D --> E["Normalize sourceKind and tier"]
  E --> F["Expose tool registry to model runtime"]
  F --> G["Tool call -> local execution -> result"]

  class A,B host;
  class C,D,E pack;
  class F,G runtime;
```

## Pack Inventory (Current Runtime Truth)

The table below reflects the actual bootstrap in `IntelligenceX.Chat.Tooling/ToolPackBootstrap.cs`.

| Pack | Descriptor ID | Source kind | Default in IX Chat | Tier | Platform |
|---|---|---|---|---|---|
| Event Log (EventViewerX) | `eventlog` | `builtin` | Yes | SensitiveRead | Windows |
| File System | `fs` | `builtin` | Yes | ReadOnly | Cross-platform |
| Reviewer Setup | `reviewersetup` | `builtin` | Yes | ReadOnly | Cross-platform |
| Email (Mailozaurr) | `email` | `builtin` | Yes (OSS pack; runtime dependency-gated) | SensitiveRead | Cross-platform |
| Office Documents (OfficeIMO) | `officeimo` | `open_source` | Yes (OSS pack; runtime dependency-gated) | ReadOnly | Cross-platform |
| PowerShell Runtime | `powershell` | `builtin` | No (OSS pack; opt-in by policy) | DangerousWrite | Windows/PowerShell hosts |
| ComputerX | `system` | `closed_source` | Yes (when available) | ReadOnly | Windows |
| ADPlayground | `ad` | `closed_source` | Yes (when available) | SensitiveRead | Windows (domain environments) |
| TestimoX | `testimox` | `closed_source` | Yes (when available) | SensitiveRead | Windows |

## IX Chat vs .NET Integration

### IX Chat

- Tool packs are loaded by the host bootstrap.
- Some packs are always available in OSS (`eventlog`, `fs`, `reviewersetup`).
- Some are optional/conditional for runtime reasons (`email` dependency gating, `powershell` safety opt-in), while still being OSS-oriented packs.
- Some are enabled by default but may not exist in OSS environments (`system`, `ad`, `testimox`).

### .NET library (custom apps)

You can build your own tool registry and register specific packs in code:

```csharp
var tools = new ToolRegistry();
tools.Register<FileSystemToolPack>();
tools.Register<EmailToolPack>();

var result = await Easy.ChatAsync("Summarize changed files", tools: tools);
```

For package-based integration, use only packs you actually reference.

## Related

- [Tool Catalog](/docs/tools/catalog/) - Pack-by-pack summary and representative tools
- [Tool Pack Governance](/docs/tools/governance/) - Naming, provenance, and delivery rules
- [IX Chat Architecture](/docs/chat/architecture/) - How packs are loaded by the host
- [Tool Calling](/docs/library/tools/) - Using tool calling in the .NET library
