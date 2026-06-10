# IX Tray Microsoft Store Publishing

This playbook prepares the packaged IX Tray app for Microsoft Store submission. It keeps the app binary build, MSIX upload artifact, Partner Center submission config, and production identity checks separate so release operators can see which layer is still missing.

## Current lane

- App project: `IntelligenceX.Tray/IntelligenceX.Tray.csproj`
- Store packaging project: `Installer/IntelligenceX.Tray.Store/IntelligenceX.Tray.Store.wapproj`
- Store manifest: `Installer/IntelligenceX.Tray.Store/Package.appxmanifest`
- PowerForge Store package config: `Build/powerforge.dotnetpublish.json` -> `StorePackages[]` -> `IntelligenceX.Tray.Store`
- Partner Center submit template: `Build/store.submit.tray.example.json` (manifest-based artifact selection; requires PSPublishModule/PowerForge `3.0.22` or newer)
- Local submit config: `Build/store.submit.tray.local.json` (ignored)
- Front-door helper: `Build/Store/Prepare-TrayStoreSubmission.ps1`

## One-time Partner Center setup

1. Reserve the app name in Partner Center.
2. Replace `Package.appxmanifest` identity values with the Partner Center package identity and publisher.
3. Keep the package version in four-part form and leave the fourth part as `0` for Store packages.
4. Create or associate a Microsoft Entra application for Store submission automation.
5. Add the Entra app to Partner Center with the needed submission role.
6. Copy `Build/store.submit.tray.example.json` to `Build/store.submit.tray.local.json`.
7. Fill `SellerId`, `TenantId`, `ClientId`, `ClientSecretEnvVar`, and `ApplicationId` in the local file. Store the secret only in the environment variable named by `ClientSecretEnvVar`.

Microsoft's current Store/MSIX docs call out the same release gates: test packages with Windows App Certification Kit, use Partner Center identity metadata when building Store packages, keep Windows 10/11 package version part four as `0`, and authenticate Store submission API calls with Microsoft Entra client credentials.

## Local readiness

The helper is intentionally thin: it invokes the PowerForge Store build and optionally calls `powerforge store submit`. Use the release gates below for identity, asset, WACK, and Partner Center readiness.

To exercise the wrapper without building or submitting:

```powershell
pwsh ./Build/Store/Prepare-TrayStoreSubmission.ps1 -SkipBuild -SubmissionMode None
```

## Build Store upload artifacts

Build the Store package artifacts through the same PowerForge lane the release wrapper uses:

```powershell
pwsh ./Build/Store/Prepare-TrayStoreSubmission.ps1 -SubmissionMode None
```

This builds `FrameworkDependent` Store upload packages for `win-x64` and `win-arm64` by default and cleans the IX Tray Store output root first. The later submit step selects artifacts from `Artifacts/DotNetPublish/manifest.json`, not by recursively scanning Store output folders.

To build only x64:

```powershell
pwsh ./Build/Store/Prepare-TrayStoreSubmission.ps1 -Runtimes win-x64 -SubmissionMode None
```

## Submission dry run

After `Build/store.submit.tray.local.json` is filled with real Partner Center values:

```powershell
pwsh ./Build/Store/Prepare-TrayStoreSubmission.ps1 -SkipBuild -SubmissionMode Plan
```

Use `-SubmissionMode Validate` for a validation call without commit when supported by the current PowerForge provider.

## Production submit

Only use this after the package was built from the intended commit, the upload artifact was inspected, WACK has passed, and the Partner Center draft metadata/listing is ready:

```powershell
pwsh ./Build/Store/Prepare-TrayStoreSubmission.ps1 -SkipBuild -SubmissionMode Submit
```

## Manual PowerForge commands

The helper wraps these commands:

```powershell
pwsh ./Build/Build-Project.ps1 -SkipWorkspaceBuild -ToolsOnly -Targets IntelligenceX.Tray -Runtimes win-x64,win-arm64 -Frameworks net10.0-windows10.0.19041.0 -Styles FrameworkDependent -ToolOutputs Store
powerforge store submit --config ./Build/store.submit.tray.local.json --target IntelligenceX.Tray.Store --plan
```

## Release gates

- `Package.appxmanifest` identity and publisher match Partner Center.
- `Package.appxmanifest` version is monotonic and ends in `.0`.
- Store assets exist at the expected base dimensions.
- `Build/store.submit.tray.local.json` has no placeholder Partner Center values.
- Store build records the expected `*.msixupload` files in `Artifacts/DotNetPublish/manifest.json`.
- Windows App Certification Kit passes on the upload package.
- Manual smoke install/open was done on clean Windows hardware or VM.
- `runFullTrust` justification and submission notes are ready in Partner Center.

## Microsoft references

- [App package requirements for MSIX apps](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)
- [Package identity overview](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/package-identity-overview)
- [Sign an MSIX package](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview)
- [Manage Microsoft Entra applications in Partner Center](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/manage-azure-ad-applications-in-partner-center)
- [Manage app submissions with Microsoft Store services](https://learn.microsoft.com/en-us/windows/uwp/monetize/manage-app-submissions)
