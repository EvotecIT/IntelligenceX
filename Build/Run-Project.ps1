param(
    [string] $ConfigPath = "$PSScriptRoot\run.profiles.json",
    [ValidateSet('Chat.Host', 'Chat.App', 'Chat.Service', 'Tray', 'Cli')]
    [string] $Target,
    [switch] $ListTargets,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string] $Framework,
    [switch] $NoBuild,
    [string[]] $AllowRoot,
    [switch] $IncludePrivateToolPacks,
    [string] $TestimoXRoot,
    [string[]] $ExtraArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName
. (Join-Path $repoRoot 'Build\Internal\Build.ScriptSupport.ps1')
. (Join-Path $repoRoot 'Build\Internal\Resolve-PowerForgeCli.ps1')
$cli = Resolve-PowerForgeCliInvocation -RepoRoot $repoRoot
$runArgs = [System.Collections.Generic.List[string]]::new()
$runArgs.AddRange([string[]] $cli.Prefix)
$runArgs.Add('run')
$runArgs.Add('--config')
$runArgs.Add($ConfigPath)

if ($ListTargets -or [string]::IsNullOrWhiteSpace($Target)) {
    $runArgs.Add('--list')
} else {
    $runArgs.Add('--target')
    $runArgs.Add($Target)
}

if (-not $ListTargets -and -not [string]::IsNullOrWhiteSpace($Target)) {
    $runArgs.Add('--configuration')
    $runArgs.Add($Configuration)
}

if (-not [string]::IsNullOrWhiteSpace($Framework)) {
    $runArgs.Add('--framework')
    $runArgs.Add($Framework)
}
if ($NoBuild) {
    $runArgs.Add('--no-build')
}
if ($AllowRoot -and $AllowRoot.Count -gt 0) {
    foreach ($root in $AllowRoot) {
        if (-not [string]::IsNullOrWhiteSpace($root)) {
            $runArgs.Add('--allow-root')
            $runArgs.Add($root)
        }
    }
}
if ($IncludePrivateToolPacks) {
    $runArgs.Add('--include-private-tool-packs')
}
if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
    $runArgs.Add('--testimox-root')
    $runArgs.Add($TestimoXRoot)
}
if ($ExtraArgs -and $ExtraArgs.Count -gt 0) {
    foreach ($arg in $ExtraArgs) {
        $runArgs.Add('--extra-arg')
        $runArgs.Add($arg)
    }
}

Write-Header 'Run Project'
Write-Step "Run profiles: $ConfigPath"
if ($ListTargets -or [string]::IsNullOrWhiteSpace($Target)) {
    Write-Step 'Listing available targets.'
} else {
    Write-Step "Target: $Target"
    Write-Step "Configuration: $Configuration"
}
Write-Step ("Command: {0}" -f (Format-CommandLine -Command $cli.Command -Arguments $runArgs))

& $cli.Command @runArgs
if ($LASTEXITCODE -ne 0) {
    throw ("PowerForge run failed with exit code ${LASTEXITCODE}.{0}Hint: run with -ListTargets to confirm the target name, or use -NoBuild when you only want to launch an already-built app." -f [Environment]::NewLine)
}
