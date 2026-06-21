# Build IntelligenceX.Reviewer release assets through the unified PowerForge path.

[CmdletBinding()] param(
    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [string] $Framework = 'net8.0',

    [string[]] $Runtimes = @('win-x64','linux-x64','osx-x64'),

    [string] $StageRoot,

    [switch] $PublishGitHub
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$buildProjectScript = Join-Path $repo 'Build\Build-Project.ps1'
$configPath = Join-Path $repo 'Build\release.reviewer.json'

$parameters = @{
    ConfigPath = $configPath
    ToolsOnly = $true
    Targets = @('IntelligenceX.Reviewer')
    Runtimes = $Runtimes
    Frameworks = @($Framework)
    Styles = @('FrameworkDependent')
    Configuration = $Configuration
    SkipWorkspaceBuild = $true
}

if (-not [string]::IsNullOrWhiteSpace($StageRoot)) {
    $parameters.StageRoot = $StageRoot
}
if ($PublishGitHub) {
    $parameters.PublishToolGitHub = $true
}

& pwsh -NoProfile -File $buildProjectScript @parameters
if ($LASTEXITCODE -ne 0) {
    throw "Reviewer release build failed with exit code ${LASTEXITCODE}."
}
