# Release Matrix

Use this as the quick "which command do I run?" map.

## Front Doors

| Need | Command | Notes |
| --- | --- | --- |
| See what will build/publish | `pwsh ./Build/Build-Project.ps1 -Plan` | safest starting point |
| Publish packages + supported app/tool targets | `pwsh ./Build/Build-Project.ps1` | unified PowerForge path |
| Produce full client release folder | `pwsh ./Build/Build-Release.ps1 -Runtime win-x64 -Configuration Release` | wraps workspace validation + release staging |
| Run local apps/tools | `pwsh ./Build/Run-Project.ps1 -ListTargets` | then pick `-Target` |

## Publish Styles

| Style | What it means | Use when |
| --- | --- | --- |
| `FrameworkDependent` | smaller output, target machine needs the matching .NET runtime | internal/dev machines or managed environments |
| `PortableCompat` | self-contained output, larger size, best portability | client delivery, bundles, MSI, machines without guaranteed runtime |
| `Aot*` | native-style publish options supported by PowerForge, but not enabled in IX yet | only after target-specific validation |

Useful release-time overrides on the unified path:
- `-IncludeSymbols`
- `-ToolOutputs Portable,Installer` or `-SkipToolOutputs Installer` on `Build-Project.ps1` when you want output-level selection without dropping to granular scripts
- `-SignInstaller`
  Normal case: set `CERT_THUMBPRINT` once and let the wrapper resolve it automatically.
  Override case: pass `-SignThumbprint` explicitly for one run.
- advanced signing inputs like `-SignSubjectName`, `-SignTimestampUrl`, `-SignCsp`, `-SignKeyContainer` only when needed
- signing policy knobs: `-SignOnMissingTool Warn|Fail|Skip` and `-SignOnFailure Warn|Fail|Skip`

## Current Unified Targets

| Target | Recommended style | Why |
| --- | --- | --- |
| `IntelligenceX.Cli` | `FrameworkDependent` for dev, `PortableCompat` for distribution | cross-platform CLI |
| `IntelligenceX.Tray` | `PortableCompat` | easiest end-user setup |
| `IntelligenceX.Chat.Host` | `FrameworkDependent` for internal use, `PortableCompat` when shipping | host-only chat runtime |
| `IntelligenceX.Chat.Service` | `PortableCompat` | sidecar/runtime payload |
| `IntelligenceX.Chat.App` | `PortableCompat` | drives portable bundle + MSI flow |

## Full Release Path

Normal `Frontend app` release stays on the unified PowerForge path.

`Build-Release.ps1` now maps package / portable / installer choices onto PowerForge release output selection instead of re-implementing the release workflow in PowerShell.

`-Frontend host` is not modeled by the unified release config yet, so use the legacy helpers directly for that path.

## Legacy Helpers Still Kept

These are still valid, but they are no longer the default path:

- `Build/Advanced/Publish-Plugins.ps1`
- `Build/Advanced/Package-Portable.ps1`
- `Build/Advanced/Build-Installer.ps1`

Keep them for manual recovery, special-case signing, and edge-case release work.

Useful portable smoke options on the fallback path:
- `-SmokeScenarioPreset runtime-only`
- `-SmokeScenarioPreset runtime-and-toolful`
- Example:
  `pwsh ./Build/Advanced/Package-Portable.ps1 -Frontend host -Runtime win-x64 -PluginMode all -IncludePrivateToolPacks -TestimoXRoot C:\Support\GitHub\TestimoX -IncludePortableHelpers -SmokeScenarioPreset runtime-and-toolful`
