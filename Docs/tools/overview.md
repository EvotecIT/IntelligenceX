---
title: IX Tools Overview
description: Specialized tool packs that give AI assistants real-world capabilities.
---

# IX Tools

IX Tools is a collection of specialized tool packs that give AI assistants the ability to interact with real-world systems. Each pack is a standalone NuGet package that registers tools with the IntelligenceX tool registry.

## Design Goals

- **One Pack, One Concern** -- Each tool pack handles a single domain (email, AD, files, etc.)
- **Zero Config Defaults** -- Tools work out of the box with sensible defaults
- **Provider Agnostic** -- Tools work with any AI provider that supports function calling
- **Dependency Minimal** -- Packs only reference what they need; no heavy shared framework

## How Tool Packs Work

1. Install a tool pack NuGet package
2. Register it with the IntelligenceX tool registry
3. The AI provider sees the available tools and their schemas
4. When the AI decides to use a tool, IntelligenceX executes it locally
5. Results are returned to the AI for the next response

```csharp
// Register a tool pack
var tools = new ToolRegistry();
tools.Register<EmailToolPack>();
tools.Register<FileSystemToolPack>();

// Use with Easy.ChatAsync
var result = await Easy.ChatAsync("Send a summary email to the team", tools: tools);
```

## Available Tool Packs

| Pack | Package | Platform |
|---|---|---|
| Email | `IntelligenceX.Tools.Email` | Cross-platform |
| Active Directory | `IntelligenceX.Tools.ActiveDirectory` | Windows |
| File System | `IntelligenceX.Tools.FileSystem` | Cross-platform |
| Web | `IntelligenceX.Tools.Web` | Cross-platform |
| Database | `IntelligenceX.Tools.Database` | Cross-platform |
| Azure | `IntelligenceX.Tools.Azure` | Cross-platform |
| Office | `IntelligenceX.Tools.Office` | Cross-platform |
| System | `IntelligenceX.Tools.System` | Windows |

See the [Tool Catalog](/docs/tools/catalog/) for detailed descriptions and tool listings.

## Repository Split

Tool packs live in separate repositories to keep dependencies isolated:

- **IntelligenceX** -- Core library, CLI, reviewer (this repo)
- **IntelligenceX.Tools.*** -- Individual tool pack repos

## Related

- [Tool Catalog](/docs/tools/catalog/) -- All 8 packs with tool listings
- [Tool Calling](/docs/library/tools/) -- How tool calling works in the .NET library
- [IX Chat](/docs/chat/overview/) -- Desktop app with tool calling support
