# Build and Publish

This repo supports two practical workflows:
- `oss`: public/open-source baseline that works in public CI
- `full-private`: includes private TestimoX-backed packs for internal development

## 1. Cleanup

Prune stale worktree metadata and optionally clean artifacts:

```powershell
pwsh ./Build/Cleanup-Workspace.ps1 -IncludeKnownSiblingRepos -FetchPrune
```

Optional destructive cleanup switches:
- `-CleanArtifacts`
- `-RemoveOrphanedWorktreeDirs`
- `-RemoveMergedWorktrees`
- `-DeleteMergedLocalBranches`

Use `-WhatIf` first when using destructive cleanup switches.

Merged-worktree cleanup defaults to `codex/` branches merged into `origin/master`.
You can tune branch selection:
- `-MergedBranchPrefix <prefix>` (for example `codex/`)
- `-SkipMergedBranchPrefixes <prefix1>,<prefix2>` (for example keep website worktrees)
  You can pass either separate values (`-SkipMergedBranchPrefixes a,b`) or repeated values (`-SkipMergedBranchPrefixes a -SkipMergedBranchPrefixes b`).

Example:

```powershell
pwsh ./Build/Cleanup-Workspace.ps1 `
  -IncludeKnownSiblingRepos `
  -FetchPrune `
  -RemoveMergedWorktrees `
  -DeleteMergedLocalBranches `
  -SkipMergedBranchPrefixes codex/wt-intelligencex-website
```

## 2. Build

OSS-safe build:

```powershell
pwsh ./Build/Build-Workspace.ps1 -Profile oss -IncludePublicTools
```

Private/full build (requires TestimoX sources):

```powershell
pwsh ./Build/Build-Workspace.ps1 -Profile full-private -TestimoXRoot C:\Support\GitHub\TestimoX -IncludeChat
```

What this runs:
- CI-equivalent build/test (`IntelligenceX.CI.slnf`)
- executable harness (`IntelligenceX.Tests.dll` net8/net10)
- public tool-pack builds
- private tool-pack builds only in `full-private`
- optional chat build (`-IncludeChat`)

## 3. Publish

CLI:

```powershell
pwsh ./Build/Publish-Cli.ps1 -Runtime win-x64 -Configuration Release -Framework net8.0
```

Chat (single app default = Host, service optional):

```powershell
pwsh ./Build/Publish-Chat.ps1 -Runtime win-x64 -Configuration Release
pwsh ./Build/Publish-Chat.ps1 -Runtime win-x64 -Configuration Release -IncludeService
pwsh ./Build/Publish-Chat.ps1 -Runtime win-x64 -Configuration Release -IncludePrivateToolPacks -TestimoXRoot C:\Support\GitHub\TestimoX
```

Portable app bundle (recommended for end users):

```powershell
pwsh ./Build/Package-Portable.ps1 -Runtime win-x64 -Configuration Release -PluginMode public
pwsh ./Build/Package-Portable.ps1 -Runtime win-x64 -Configuration Release -PluginMode all -TestimoXRoot C:\Support\GitHub\TestimoX -IncludeService
```

Folder-based plugins (recommended when shipping app bundles):

```powershell
pwsh ./Build/Export-PluginFolders.ps1 -Mode public -Configuration Release
pwsh ./Build/Export-PluginFolders.ps1 -Mode private -Configuration Release -TestimoXRoot C:\Support\GitHub\TestimoX
```

Each plugin is exported as a folder (not just one `.dll`) because dependencies are usually required beside the entry assembly.

Example layout:

```text
plugins/
  IntelligenceX.Tools.EventLog/
    IntelligenceX.Tools.EventLog.dll
    IntelligenceX.Tools.Common.dll
    <other dependencies>.dll
    ix-plugin.json
```

NuGet plugin packages (advanced/internal workflow):

```powershell
# Public packs (nuget.org friendly)
pwsh ./Build/Publish-Plugins.ps1 -Mode public -Configuration Release

# Private packs (requires TestimoXRoot/private feed)
pwsh ./Build/Publish-Plugins.ps1 -Mode private -Configuration Release -TestimoXRoot C:\Support\GitHub\TestimoX
```

Optional push:

```powershell
pwsh ./Build/Publish-Plugins.ps1 -Mode public -Push -Source https://api.nuget.org/v3/index.json -ApiKey <key>
```

## 4. One-Command Local Chat

Do not require users to start host + service manually for normal usage.

Default local command:

```powershell
pwsh ./Build/Run-Chat.ps1 -AllowRoot C:\Support\GitHub
pwsh ./Build/Run-Chat.ps1 -AllowRoot C:\Support\GitHub -IncludePrivateToolPacks -TestimoXRoot C:\Support\GitHub\TestimoX
pwsh ./Build/Run-Chat.ps1 -AllowRoot C:\Support\GitHub -PluginPath .\Artifacts\Plugins
```

This runs `IntelligenceX.Chat.Host` directly as the user-facing app.

WinUI desktop app (auto-manages local runtime):

```powershell
pwsh ./Build/Run-ChatApp.ps1 -Configuration Release
pwsh ./Build/Run-ChatApp.ps1 -Configuration Release -IncludePrivateToolPacks -TestimoXRoot C:\Support\GitHub\TestimoX
```

`Run-ChatApp.ps1` is the centralized entrypoint for desktop app startup from this repo.

Plugin discovery defaults:
- `%LOCALAPPDATA%\IntelligenceX.Chat\plugins`
- `plugins` under the host executable directory
- plus any explicit `--plugin-path`/`-PluginPath` values

Portable bundle output defaults:
- folder: `Artifacts\Portable\<runtime>\IntelligenceX.Chat-Portable-<runtime>`
- zip: same folder, timestamped zip created by `Package-Portable.ps1`

## 5. Public vs Private Tool Placement

Recommended placement:
- Public/open packs remain in this repo flow (for example: `Common`, `FileSystem`, `PowerShell`, `Email`, `EventLog`, `ReviewerSetup`)
  Public runtime dependencies for this path live in `IntelligenceX.Engines.*` projects/packages.
- Private packs and private engines (`System`, `TestimoX`, `ActiveDirectory`) should be maintained in `TestimoX` and published to a private feed

Why:
- open-source CI can build without private repos
- internal teams can continue package-first development against private repos
- downstream users can keep their own private pack repos while using public IntelligenceX core
