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

## Pack Inventory (Current Runtime Truth)

The table below reflects the actual bootstrap in `IntelligenceX.Chat.Tooling/ToolPackBootstrap.cs`.

| Pack | Descriptor ID | Source kind | Default in IX Chat | Tier | Platform |
|---|---|---|---|---|---|
| Event Log | `eventlog` | `builtin` | Yes | SensitiveRead | Windows |
| File System | `fs` | `builtin` | Yes | ReadOnly | Cross-platform |
| Reviewer Setup | `reviewer_setup` | `builtin` | Yes | ReadOnly | Cross-platform |
| Email | `email` | `builtin` | Yes (when assembly is available) | SensitiveRead | Cross-platform |
| IX.PowerShell | `powershell` | `builtin` | No (opt-in) | DangerousWrite | Windows/PowerShell hosts |
| System | `system` | `closed_source` | Yes (when available) | ReadOnly | Windows |
| Active Directory | `ad` | `closed_source` | Yes (when available) | SensitiveRead | Windows (domain environments) |
| IX.TestimoX | `testimox` | `closed_source` | Yes (when available) | SensitiveRead | Windows |

## IX Chat vs .NET Integration

### IX Chat

- Tool packs are loaded by the host bootstrap.
- Some packs are always available in OSS (`eventlog`, `fs`, `reviewer_setup`).
- Some are optional/conditional (`email` if assembly is present, `powershell` if enabled).
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
- [IX Chat Architecture](/docs/chat/architecture/) - How packs are loaded by the host
- [Tool Calling](/docs/library/tools/) - Using tool calling in the .NET library
