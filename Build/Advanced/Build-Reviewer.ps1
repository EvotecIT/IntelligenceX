# Publish IntelligenceX.Reviewer for multiple runtimes

[CmdletBinding()] param(
    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [string] $Framework = 'net8.0',

    [string[]] $Runtimes = @('win-x64','linux-x64','osx-x64'),

    [switch] $SelfContained = $true,
    [switch] $SingleFile = $true,
    [switch] $Trim,

    [switch] $Zip = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$publishScript = Join-Path $repo 'Build/Advanced/Publish-Reviewer.ps1'
if (-not (Test-Path $publishScript)) { throw "Publish script not found: $publishScript" }

foreach ($rid in $Runtimes) {
    & pwsh -File $publishScript -Runtime $rid -Configuration $Configuration -Framework $Framework -SelfContained:$SelfContained -SingleFile:$SingleFile -Trim:$Trim -Zip:$Zip
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid ($LASTEXITCODE)" }
}
