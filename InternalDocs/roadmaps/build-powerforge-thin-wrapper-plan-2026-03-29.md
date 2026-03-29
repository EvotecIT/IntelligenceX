# Build to PowerForge Thin-Wrapper Plan

Date: 2026-03-29

## Goal

Keep repo-local PowerShell only for repo-specific tasks and developer ergonomics.
Move generic build, package, bundle, installer, signing, and staging behavior into PowerForge C# so `Build/*.ps1` becomes a thin compatibility layer over declarative config plus CLI commands.

## Current Shape

Largest scripts in `Build/`:

| Lines | File | Notes |
| --- | --- | --- |
| 469 | `Build/Advanced/Package-Portable.ps1` | Fallback portable bundle orchestration. Still owns publish decisions and shell-to-shell composition. |
| 427 | `Build/Internal/Cleanup-Workspace.ps1` | Repo/worktree cleanup utility. Mostly repo maintenance, not release pipeline. |
| 400 | `Build/Advanced/Build-Installer.ps1` | MSI payload harvesting, version shaping, junction workaround, build, signing. |
| 335 | `Build/Chat/Run-ChatLiveConversation.ps1` | Repo-specific QA harness. Reasonable to keep script-driven. |
| 288 | `Build/Build-Release.ps1` | Release routing/fallback logic on top of PowerForge. |
| 286 | `Build/Chat/Run-ChatQualityPreflight.ps1` | Repo-specific QA orchestration. Reasonable to keep script-driven. |
| 245 | `Build/Internal/Complete-PortableBundle.ps1` | Bundle finalization logic that is generic enough for PowerForge. |
| 217 | `Build/Internal/Export-PluginFolders.ps1` | Plugin export/package plumbing used by bundle paths. |

## Keep in PowerShell

These are still valid as scripts because they are repo-specific workflows rather than generic build engine behavior:

- `Build/Chat/Run-Chat*.ps1`
- `Build/Chat/Test-PortableChatBundle.ps1`
- `Build/Chat/Compare-ChatScenarioReports.ps1`
- `Build/Chat/Get-ChatScenarioCoverage.ps1`
- `Build/Internal/Cleanup-Workspace.ps1`

These scripts can still be simplified over time, but they do not have to move into PowerForge first.

## Move into PowerForge

These responsibilities are generic build/publish engine behavior and should live in PowerForge C#:

### Portable bundle finalization

Current owner:
- `Build/Internal/Complete-PortableBundle.ps1`

Should become PowerForge features:
- bundle helper launcher generation
- bundle README generation
- plugin folder export/archive conversion
- bundle metadata manifest generation
- lean-bundle cleanup rules such as removing `createdump.exe` and symbols

### Portable publish orchestration

Current owner:
- `Build/Advanced/Package-Portable.ps1`

Should become PowerForge features:
- target-aware primary app and sidecar publish composition
- frontend-specific publish behavior (`app` vs `host`)
- optional service inclusion
- zip output and naming
- post-publish smoke hook support

### MSI installer pipeline

Current owner:
- `Build/Advanced/Build-Installer.ps1`

Should become PowerForge features:
- harvest manifest generation
- MSI version normalization
- short-path/junction workaround for long payload paths
- WiX build invocation
- signing tool discovery and signing policy
- installer manifest emission

### Release fallback routing

Current owner:
- `Build/Build-Release.ps1`

Should become PowerForge features:
- release-mode graph selection
- optional package/portable/installer inclusion flags
- release-stage output layout
- shared signing/default resolution

## Thin Wrappers That Are Already Fine

These files are close to the desired end state:

- `Build/Run-Project.ps1`
- `Build/Build-Project.ps1`
- `Build/Build-Workspace.ps1`
- `Build/Internal/Resolve-PowerForgeCli.ps1`
- `Build/Internal/Build.ScriptSupport.ps1`

The main remaining work is not to delete these wrappers, but to reduce what the advanced/fallback scripts still own.

## Recommended Order

1. Move bundle finalization from `Complete-PortableBundle.ps1` into PowerForge.
2. Move portable publish orchestration from `Package-Portable.ps1` into PowerForge using the same config source as the normal publish path.
3. Move MSI harvest/build/sign flow from `Build-Installer.ps1` into PowerForge.
4. Collapse `Build-Release.ps1` into a thin wrapper over a single PowerForge release mode.
5. Keep chat QA scripts repo-local, but make them call stable PowerForge/run entrypoints instead of bespoke publish helpers.

## Immediate Cleanup Wins

- Remove duplicate dead code from fallback scripts after behavior is centralized.
- Add validation tests for config drift between run/publish profiles and referenced projects.
- Prefer `project-defined` for run profiles when the project already declares the authoritative TFM.
- Keep repo docs honest about which paths are truly thin wrappers and which are still legacy/fallback.

## Done in This Branch

- Fixed the Tray run-profile framework drift by changing `Build/run.profiles.json` to use `project-defined` for `Tray`.
- Added `IntelligenceX.UnitTests/RunProfileFrameworkValidationTests.cs` to catch project run-profile TFM drift.
- Removed duplicate dead bundle-finalization helpers from `Build/Advanced/Package-Portable.ps1`; that behavior now clearly belongs to `Build/Internal/Complete-PortableBundle.ps1`.
