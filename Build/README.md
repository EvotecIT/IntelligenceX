# Build Folder Guide

Use these as the main entrypoints:

- `Build-Project.ps1`
  - unified package + app/tool publish path
  - thin wrapper over `powerforge release`
  - workspace preflight is now declared in `release.json` + `workspace.validation.json`, not hard-coded in the script
- `Build-Release.ps1`
  - repo-level release wrapper when you want workspace validation + package assets + portable chat bundle + MSI
  - default `Frontend app` flow now uses the unified `Build-Project.ps1` / PowerForge path to stage a categorized release folder directly
  - only falls back to legacy helpers for edge cases like host mode or deliberately skipping granular steps
  - when it falls back, it now prints the reason instead of silently switching paths
- `Build-Workspace.ps1`
  - thin compatibility wrapper over `powerforge workspace validate`
- `workspace.validation.json`
  - declarative workspace-validation contract used by `Build-Workspace.ps1`
  - also consumed by `Build-Project.ps1` through unified `powerforge release`
- `Run-Project.ps1`
  - unified local runtime entrypoint for `Chat.Host`, `Chat.App`, `Chat.Service`, `Tray`, and `Cli`
  - prints the resolved PowerForge command so it is easier to see what will actually run
- `run.profiles.json`
  - declarative local run targets used by `Run-Project.ps1`
- `Release-Matrix.md`
  - short command/style matrix for publish and release choices

Folder layout:

- `Build\`
  - front-door commands and release configs only
- `Build\Advanced\`
  - fallback/manual release helpers
- `Build\Internal\`
  - implementation helpers that other scripts call
- `Build\Chat\`
  - specialized chat runtime, scenario, and QA harness scripts

Examples:

```powershell
pwsh ./Build/Build-Project.ps1 -Plan
powerforge workspace validate --config ./Build/workspace.validation.json --list
powerforge workspace validate --config ./Build/workspace.validation.json --profile oss --enable-feature chat
pwsh ./Build/Build-Project.ps1 -SkipWorkspaceBuild -ToolsOnly -Targets IntelligenceX.Tray
pwsh ./Build/Build-Project.ps1 -Configuration Debug -ToolsOnly -Targets IntelligenceX.Chat.App -Styles PortableCompat
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Chat.App -Styles PortableCompat -IncludeSymbols
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Chat.App -Styles PortableCompat -SignInstaller
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Chat.App -Styles PortableCompat -SignInstaller -SignThumbprint <thumbprint>
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Chat.App -Styles PortableCompat -SignInstaller -SignSubjectName "Evotec Code Signing" -SignOnFailure Warn
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Chat.App -Styles PortableCompat
pwsh ./Build/Build-Project.ps1 -StageRoot ./Artifacts/Releases/demo -SkipChecksums
pwsh ./Build/Build-Release.ps1 -Runtime win-x64 -Configuration Release
pwsh ./Build/Run-Project.ps1 -ListTargets
pwsh ./Build/Run-Project.ps1 -Target Chat.App
pwsh ./Build/Run-Project.ps1 -Target Chat.Host -AllowRoot C:\Support\GitHub
pwsh ./Build/Run-Project.ps1 -Target Tray
pwsh ./Build/Run-Project.ps1 -Target Cli -ExtraArgs setup,wizard
```

Tray Store package:

```powershell
pwsh ./Build/Build-Project.ps1 -ToolsOnly -Targets IntelligenceX.Tray -Runtimes win-x64 -Frameworks net10.0-windows10.0.19041.0 -Styles FrameworkDependent
dotnet exec C:\Support\GitHub\PSPublishModule\PowerForge.Cli\bin\Release\net10.0\PowerForge.Cli.dll store submit --config ./Build/store.submit.tray.example.json --target IntelligenceX.Tray.Store --plan
```

Notes:
- the packaging project is `Installer/IntelligenceX.Tray.Store/IntelligenceX.Tray.Store.wapproj`
- the checked-in Store manifest identity/publisher is placeholder metadata for local packaging validation and must be replaced with the real Partner Center identity before production submission
- `Build/store.submit.tray.example.json` is only an example; fill in the real `SellerId`, `TenantId`, `ClientId`, secret, and Partner Center `ApplicationId`

`TestimoXRoot` is now resolved centrally for normal builds:
- `Directory.Build.props` picks up `TESTIMOX_ROOT` when set.
- otherwise the repo falls back to sibling `..\TestimoX` / `..\..\TestimoX` layouts.
- use `-TestimoXRoot` only when you need to override that default.

Signing is also resolved centrally for the normal case:
- prefer `CERT_THUMBPRINT` when you want one default code-signing certificate for the repo
- `Build-Project.ps1 -SignInstaller` and `Build-Release.ps1 -SignInstaller` now use that thumbprint automatically
- `-SignThumbprint` still wins when you need to override it for one run
- `-SignSubjectName`, `-SignCsp`, and `-SignKeyContainer` remain available as advanced escape hatches

Keep these specialist helpers:

- `Advanced\Publish-Plugins.ps1`
  - direct NuGet-style tool-pack packaging helper
- `Advanced\Package-Portable.ps1`
  - fallback/manual portable bundle helper
- `Advanced\Build-Installer.ps1`
  - fallback/manual MSI helper
- `Chat\Run-Chat*.ps1`
  - chat runtime and validation harness scripts
- `Chat\Compare-ChatScenarioReports.ps1`, `Chat\Get-ChatScenarioCoverage.ps1`
  - scenario analysis helpers
- `Internal\Export-PluginFolders.ps1`, `Internal\Complete-PortableBundle.ps1`
  - internal plumbing reused by the publish/bundle scripts
- `Internal\Resolve-TestimoXRoot.ps1`
  - shared private-engine root resolver used by workspace/plugin scripts
- `Internal\Resolve-ReleaseDefaults.ps1`
  - shared release helpers used by portable/installer/release scripts
- `Internal\Build.ScriptSupport.ps1`
  - shared console output + strict invocation helpers used by the front-door scripts

The main workspace-validation logic is no longer owned by `Build-Workspace.ps1`, and the normal publish path no longer owns that orchestration either.
`Build-Workspace.ps1` now forwards into `powerforge workspace validate`, and `Build-Project.ps1` forwards into `powerforge release`, so the readable source of truth lives in `workspace.validation.json`, `release.json`, and `powerforge.dotnetpublish.json`.
`Build-Release.ps1` now relies on the same unified release engine for the normal app flow, including final release staging into `nuget`, `portable`, `installer`, `tools`, and `metadata`.

The old standalone publish wrappers for CLI/chat/tray were removed because `Build-Project.ps1` now covers those targets through PowerForge.
