---
title: Tool Pack Governance
description: Naming, metadata ownership, source semantics, and client delivery rules for IntelligenceX tool packs.
---

# Tool Pack Governance

This document defines how tool packs must declare metadata and how hosts should consume it.

## Production Safety Notice

- Tool-pack enablement can grant high-impact capabilities (for example command execution or sensitive data access).
- Do not run permissive tool policies directly against production systems by default.
- Require explicit approval workflows, least-privilege credentials, environment isolation, and audit logging before production rollout.

## 1. Metadata Ownership

Tool-pack identity and presentation metadata must originate in the pack descriptor (`ToolPackDescriptor`) inside `IntelligenceX.Tools.*`.

Required descriptor fields:

- `Id` (stable, machine-readable)
- `Name` (human-readable)
- `Description` (short UX-ready summary)
- `SourceKind` (`builtin`, `open_source`, `closed_source`)
- `Tier` and `IsDangerous`

Host/app code should consume descriptor metadata and avoid pack-name hardcoding.

## 2. Pack ID Rules

- Use lowercase canonical ids in descriptors.
- Keep ids stable over time.
- Use aliases only in normalization layers when backward compatibility is needed.
- Tool ownership should be registered as `tool -> packId` during pack registration, not inferred from UI naming heuristics.

## 3. Naming Rules

- Pack `Name` should be user-facing and consistent.
- Prefer product/provider-first naming for engine-backed packs so ownership is explicit.
- Preferred format: `Product` or `Product - Capability` (for example `ComputerX`, `ADPlayground`, `EventViewerX - Event Log`).
- Keep domain/capability context in `Description` (and tags/IDs), not hidden in inconsistent name formats.

## 4. Source Semantics

`SourceKind` is provenance metadata and must not be used as runtime load-state:

- `builtin`: ships with core distribution/bootstrap path
- `open_source`: from open-source package/plugin
- `closed_source`: from private/proprietary package/plugin

Runtime state (for example `Loaded`, `Disabled`, `Partial`) must be shown separately from provenance.

## 4.1 Closed-Source License Boundary

For packs distributed as `closed_source`:

- They are private/proprietary distributions and are not part of the public OSS package baseline.
- Default policy is IX Chat usage only (when those packs are present in a licensed/private build).
- Using those packs outside IX Chat (for example in external custom hosts/services) requires a separate license.
- Public docs and marketing copy must not imply `closed_source` packs are generally reusable in arbitrary third-party hosts by default.

## 5. Tool Display Rules

- Prefer typed metadata from contracts (`PackId`, `PackName`, `PackDescription`, `PackSourceKind`) over string parsing.
- If metadata is missing, place tools in an explicit `Uncategorized` bucket and treat as a contract gap to fix at source.
- Sort pack groups deterministically (display-name ascending).

## 6. Build and Delivery to Clients

### Build the solution

```powershell
dotnet build IntelligenceX.sln -c Release
```

### Package plugin NuGets

```powershell
pwsh ./Build/Publish-Plugins.ps1 -Mode public -Configuration Release
```

### Export folder-based plugins

```powershell
pwsh ./Build/Export-PluginFolders.ps1 -Mode public -Configuration Release
```

Optional diagnostics export (keeps symbols):

```powershell
pwsh ./Build/Export-PluginFolders.ps1 -Mode public -Configuration Release -IncludeSymbols
```

Default outputs:

- NuGet packages: `Artifacts/NuGet`
- Folder plugins: `Artifacts/Plugins/<PackageId>/`

Folder plugin delivery contract:

- include pack assembly and dependencies
- include `ix-plugin.json` with `sourceKind` and `entryAssembly`
- strip debug symbol files (`*.pdb`) in Release exports by default
- client drops plugin folder into plugin search roots used by `ToolPackBootstrap`

## 7. Enforcement

- New packs must include descriptor metadata before merge.
- Host-side fallback heuristics should be minimal and temporary.
- If a tool appears uncategorized in UI, fix pack metadata/registration first.
