# Dev Bootstrap (Local Engine References)

This repo is **engine-first**: during development we prefer `ProjectReference` to local checkouts of engine repos
so tools stay thin wrappers and we iterate quickly without waiting for package releases.

## Required Repos (Sibling Checkouts)

By default, MSBuild tries to resolve these as sibling folders next to this repo:

- `IntelligenceX` (tool contract + JSON types)
- `TestimoX-master` (ADPlayground + ComputerX)
- `PSEventViewer` (EventViewerX)
- `Mailozaurr`

Optional (used only when you want to build against local sources instead of NuGet):
- None. All currently supported public engines can resolve from NuGet fallback.

## One-Time Setup Script

Run:

```powershell
pwsh -File .\scripts\bootstrap-dev.ps1
```

This will clone missing sibling repos under the parent folder of this repo.

## Overriding Paths

If your checkouts live elsewhere, set MSBuild properties via environment variables:

- `TestimoXRoot` : folder containing `ADPlayground\ADPlayground.csproj` and `ComputerX\ComputerX.csproj`
- `PSEventViewerRoot` : folder containing `Sources\EventViewerX\EventViewerX.csproj`
- `MailozaurrRoot` : folder containing `Sources\Mailozaurr\Mailozaurr.csproj`

Example:

```powershell
setx TestimoXRoot "C:\Support\GitHub\TestimoX-master\"
setx PSEventViewerRoot "C:\Support\GitHub\PSEventViewer\"
setx MailozaurrRoot "C:\Support\GitHub\Mailozaurr\"
```

Then restart your terminal/IDE so MSBuild picks up the updated environment variables.

