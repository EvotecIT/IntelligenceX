# Build and Publish

This repo supports two practical workflows:
- `oss`: public/open-source baseline that works in public CI
- `full-private`: includes private TestimoX-backed packs for internal development

## 1. Cleanup

Prune stale worktree metadata and optionally clean artifacts:

```powershell
pwsh ./Build/Internal/Cleanup-Workspace.ps1 -IncludeKnownSiblingRepos -FetchPrune
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
pwsh ./Build/Internal/Cleanup-Workspace.ps1 `
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
powerforge workspace validate --config ./Build/workspace.validation.json --profile oss
```

Private/full build (requires TestimoX sources):

```powershell
pwsh ./Build/Build-Workspace.ps1 -Profile full-private -TestimoXRoot C:\Support\GitHub\TestimoX -IncludeChat
powerforge workspace validate --config ./Build/workspace.validation.json --profile full-private --enable-feature chat --testimox-root C:\Support\GitHub\TestimoX
```

What this runs:
- CI-equivalent build/test (`IntelligenceX.CI.slnf`)
- executable harness (`IntelligenceX.Tests.dll` net8/net10)
- public tool-pack builds
- private tool-pack builds only in `full-private`
- optional chat build (`-IncludeChat`)

The readable source of truth is now `Build/workspace.validation.json`.
`Build-Workspace.ps1` is kept as a thin wrapper so existing habits still work, but the validation workflow itself now lives in PowerForge.

## 3. Publish

Unified package release flow (recommended first):

```powershell
pwsh ./Build/Build-Project.ps1 -Plan
pwsh ./Build/Build-Project.ps1
pwsh ./Build/Build-Project.ps1 -Configuration Debug -ToolsOnly -Targets IntelligenceX.Chat.App -Styles PortableCompat
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Chat.App -Styles PortableCompat -IncludeSymbols
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Chat.App -Styles PortableCompat -SignInstaller -SignThumbprint <thumbprint>
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Chat.App -Styles PortableCompat -SignInstaller -SignSubjectName "Evotec Code Signing" -SignOnFailure Warn
pwsh ./Build/Build-Project.ps1 -PublishProjectGitHub
pwsh ./Build/Build-Project.ps1 -ToolsOnly -PublishToolGitHub
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Chat.Host -Styles FrameworkDependent
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Chat.Service -Styles PortableCompat
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Chat.App -Styles PortableCompat
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Chat.App -Styles PortableCompat -PublishToolGitHub
```

What this covers today:
- workspace preflight via the unified `release.json -> WorkspaceValidation -> workspace.validation.json` path unless `-SkipWorkspaceBuild`
- workspace/preflight failures now include hints for `-SkipTests`, `-SkipHarness`, and `-SkipWorkspaceBuild` so it is clearer whether the problem is validation or publish
- plugin NuGet packaging through PowerForge unified `release.json`
- `IntelligenceX.Cli`, `IntelligenceX.Tray`, `IntelligenceX.Chat.Host`, `IntelligenceX.Chat.Service`, and `IntelligenceX.Chat.App` publish/zip through PowerForge DotNetPublish
- `IntelligenceX.Chat.App` portable publish composes the client bundle through PowerForge, including the service sidecar, plugin payload, helper launchers, bundle metadata, zip, and WiX MSI
- style choice through PowerForge (`FrameworkDependent` for smaller runtime-required outputs, `PortableCompat` for self-contained outputs)
- symbol-preserving outputs and MSI signing can now be requested through the unified `Build-Project.ps1` / `powerforge release` path as well
- repo-level release staging can now stay on the unified path as well through `-StageRoot` / `Build-Release.ps1`, which copies assets into `nuget`, `portable`, `installer`, `tools`, and `metadata` without a repo-local manifest-copy helper
- signing policy can now be selected from the unified path as well (`Warn` / `Fail` / `Skip`), which is useful when USB-token middleware like SafeNet may pop interactive PIN/password UI
- optional GitHub release publishing for the package asset set
- optional GitHub release publishing for CLI/tray/chat assets, including MSI outputs when produced by DotNetPublish

Quick reference:
- `Build/Release-Matrix.md` now summarizes the front-door commands, style choices, unified targets, and the remaining legacy fallback triggers on one page.

`TestimoXRoot` resolution:
- `Directory.Build.props` now defines the default `TestimoXRoot` from `TESTIMOX_ROOT` first, then sibling `..\TestimoX` / `..\..\TestimoX` layouts.
- `Build-Project.ps1` and `Build-Release.ps1` still accept `-TestimoXRoot`, but that should now be the exception rather than the normal path.

Current staged notes:
- WinAppSDK single-file publish still has app-specific runtime expectations, so keep validating the portable app output when changing startup/bootstrap code.
- `Build-Release.ps1` remains the repo-specific full release wrapper when you want workspace validation, package assets, portable chat bundle, MSI, and release manifests/checksums in one command.
- `Build-Release.ps1` is now a thin wrapper over `Build-Project.ps1` for the normal `Frontend app` release flow, and PowerForge itself stages the produced package/portable/MSI assets into the release folder.
- `Build-Release.ps1` no longer re-implements skip/fallback orchestration in PowerShell; unsupported cases like `Frontend host` now fail fast and point callers at the advanced helpers directly.
- `Build\Advanced\Package-Portable.ps1` remains a valid fallback/manual entrypoint, but it now reuses the same bundle-finishing helper as the PowerForge path instead of owning that logic outright.
- `Build\Advanced\Build-Installer.ps1` remains a valid fallback/manual entrypoint, but the default MSI path is now `Build-Project.ps1` -> PowerForge DotNetPublish.
- `Build\powerforge.dotnetpublish.json` is now the active unified CLI/tray/chat-host/chat-service/chat-app publish config.
- `Build\release.json` now also owns the normal workspace-preflight hook through `WorkspaceValidation`, so `Build-Project.ps1` stays thin.
- `Build\Internal\Resolve-TestimoXRoot.ps1` is now the shared resolver used by workspace/plugin scripts instead of duplicating that logic in multiple files.
- `Build\Internal\Resolve-ReleaseDefaults.ps1` is now the shared helper for release-specific defaults like the primary executable name and the TestimoX signing-thumbprint fallback.
- `Build\Internal\Build.ScriptSupport.ps1` is now the shared helper for front-door script console output plus strict `dotnet` / nested-script invocation.
- PowerForge also supports AOT styles, but they are not enabled in IX config until each target is validated for AOT compatibility.

Full release bundle (recommended for client delivery):

```powershell
pwsh ./Build/Build-Release.ps1 -Runtime win-x64 -Configuration Release -TestimoXRoot C:\Support\GitHub\TestimoX
```

This single command orchestrates:
- workspace validation build (`full-private` profile by default)
- plugin NuGet packaging (`public + private`)
- portable chat bundle (`PluginMode all`, closed-source included)
- MSI installer build from the portable payload
- release manifest + SHA256 checksums

Default output:
- `Artifacts\Releases\<release-id>\nuget`
- `Artifacts\Releases\<release-id>\portable`
- `Artifacts\Releases\<release-id>\installer`
- `Artifacts\Releases\<release-id>\metadata`
- `Artifacts\Releases\<release-id>\release-manifest.json`
- `Artifacts\Releases\<release-id>\SHA256SUMS.txt`

Installer-only (build from an existing portable payload or package one on demand):

```powershell
pwsh ./Build/Advanced/Build-Installer.ps1 -Runtime win-x64 -Configuration Release -TestimoXRoot C:\Support\GitHub\TestimoX
```

Installer project files:
- `Installer/IntelligenceX.Chat/IntelligenceX.Chat.Installer.wixproj`
- `Installer/IntelligenceX.Chat/IntelligenceX.Chat.wxs`

CLI:

```powershell
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Cli -Runtimes win-x64 -Frameworks net8.0 -Styles PortableCompat
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Cli -Runtimes win-x64 -Frameworks net8.0 -Styles FrameworkDependent
```

Chat (single app default = Host, service optional):

```powershell
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Chat.Host -Runtimes win-x64 -Frameworks net10.0-windows -Styles FrameworkDependent
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Chat.Service -Runtimes win-x64 -Frameworks net10.0-windows -Styles PortableCompat
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Chat.App -Runtimes win-x64 -Frameworks net8.0-windows10.0.26100.0 -Styles PortableCompat
```

Tray app:

```powershell
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Tray -Runtimes win-x64 -Frameworks net10.0-windows10.0.19041.0 -Styles PortableCompat
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Tray -Runtimes win-x64 -Frameworks net10.0-windows10.0.19041.0 -Styles FrameworkDependent
```

Tray Store package:

```powershell
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Tray -Runtimes win-x64 -Frameworks net10.0-windows10.0.19041.0 -Styles FrameworkDependent
dotnet exec C:\Support\GitHub\PSPublishModule\PowerForge.Cli\bin\Release\net10.0\PowerForge.Cli.dll store submit --config ./Build/store.submit.tray.example.json --target IntelligenceX.Tray.Store --plan
```

Store notes:
- the packaging project is `Installer/IntelligenceX.Tray.Store/IntelligenceX.Tray.Store.wapproj`
- PowerForge now routes `*.wapproj` Store builds through Visual Studio MSBuild automatically
- the checked-in app manifest uses placeholder identity/publisher values for local validation; replace them with the real Partner Center identity before submitting

Portable app bundle (recommended for end users):

```powershell
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Chat.App -Styles PortableCompat
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Chat.App -Styles PortableCompat -PublishToolGitHub
```

Fallback/manual bundle path:

```powershell
pwsh ./Build/Advanced/Package-Portable.ps1 -Runtime win-x64 -Configuration Release -PluginMode public
pwsh ./Build/Advanced/Package-Portable.ps1 -Runtime win-x64 -Configuration Release -PluginMode all -TestimoXRoot C:\Support\GitHub\TestimoX -IncludeService
```

Folder-based plugins (recommended when shipping app bundles):

```powershell
pwsh ./Build/Internal/Export-PluginFolders.ps1 -Mode public -Configuration Release
pwsh ./Build/Internal/Export-PluginFolders.ps1 -Mode private -Configuration Release -TestimoXRoot C:\Support\GitHub\TestimoX
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
pwsh ./Build/Advanced/Publish-Plugins.ps1 -Mode public -Configuration Release

# Private packs (requires TestimoXRoot/private feed)
pwsh ./Build/Advanced/Publish-Plugins.ps1 -Mode private -Configuration Release -TestimoXRoot C:\Support\GitHub\TestimoX
```

Optional push:

```powershell
pwsh ./Build/Advanced/Publish-Plugins.ps1 -Mode public -Push -Source https://api.nuget.org/v3/index.json -ApiKey <key>
```

## 4. One-Command Local Chat

Do not require users to start host + service manually for normal usage.

Unified local runner:

```powershell
pwsh ./Build/Run-Project.ps1 -ListTargets
pwsh ./Build/Run-Project.ps1 -Target Chat.App
pwsh ./Build/Run-Project.ps1 -Target Chat.Host -AllowRoot C:\Support\GitHub
pwsh ./Build/Run-Project.ps1 -Target Chat.Service -AllowRoot C:\Support\GitHub
pwsh ./Build/Run-Project.ps1 -Target Tray
pwsh ./Build/Run-Project.ps1 -Target Cli -ExtraArgs setup,wizard
```

`Run-Project.ps1` now prints the resolved run-profiles path and the exact PowerForge command it is about to launch, so it is easier to see what `Tray`, `Chat.App`, or `Cli` actually map to.

Specialized chat commands:

```powershell
pwsh ./Build/Chat/Run-Chat.ps1 -AllowRoot C:\Support\GitHub
pwsh ./Build/Chat/Run-Chat.ps1 -AllowRoot C:\Support\GitHub -IncludePrivateToolPacks -TestimoXRoot C:\Support\GitHub\TestimoX
pwsh ./Build/Chat/Run-Chat.ps1 -AllowRoot C:\Support\GitHub -PluginPath .\Artifacts\Plugins
```

This runs `IntelligenceX.Chat.Host` directly as the user-facing app.

WinUI desktop app (auto-manages local runtime):

```powershell
pwsh ./Build/Chat/Run-ChatApp.ps1 -Configuration Release
pwsh ./Build/Chat/Run-ChatApp.ps1 -Configuration Release -IncludePrivateToolPacks -TestimoXRoot C:\Support\GitHub\TestimoX
```

`Run-Project.ps1` is the top-level runtime entrypoint. `Build\Chat\Run-Chat.ps1` and `Build\Chat\Run-ChatApp.ps1` remain the specialized chat-focused runners.

Plugin discovery defaults:
- `%LOCALAPPDATA%\IntelligenceX.Chat\plugins`
- `plugins` under the host executable directory
- plus any explicit `--plugin-path`/`-PluginPath` values

Portable bundle output defaults:
- folder: `Artifacts\Portable\<runtime>\IntelligenceX.Chat-Portable-<runtime>`
- zip: `Artifacts\Portable\<runtime>\IntelligenceX.Chat-Portable-<runtime>.zip`
- MSI: `Installer\IntelligenceX.Chat\bin\x64\Release\IntelligenceX.Chat.Installer.msi`

## 5. Public vs Private Tool Placement

Recommended placement:
- Public/open packs remain in this repo flow (for example: `Common`, `FileSystem`, `PowerShell`, `Email`, `EventLog`, `ReviewerSetup`)
  Public runtime dependencies for this path live in `IntelligenceX.Engines.*` projects/packages.
- Private packs and private engines (`System`, `TestimoX`, `ActiveDirectory`) should be maintained in `TestimoX` and published to a private feed

Why:
- open-source CI can build without private repos
- internal teams can continue package-first development against private repos
- downstream users can keep their own private pack repos while using public IntelligenceX core
