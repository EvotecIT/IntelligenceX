param(
    [ValidateSet('win-x64', 'win-arm64')] [string[]] $Runtimes = @('win-x64', 'win-arm64'),
    [ValidateSet('None', 'Plan', 'Validate', 'Submit')] [string] $SubmissionMode = 'Plan',
    [string] $SubmitConfigPath = 'Build/store.submit.tray.local.json',
    [switch] $UseExampleSubmitConfig,
    [switch] $SkipBuild,
    [switch] $KeepStoreOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Get-Item (Join-Path $PSScriptRoot '..\..')).FullName

if (-not $SkipBuild) {
    $storeOutputRoot = Join-Path $repoRoot 'Artifacts\DotNetPublish\Store\IntelligenceX.Tray.Store'
    if (-not $KeepStoreOutput -and (Test-Path -LiteralPath $storeOutputRoot)) {
        $resolvedStoreOutputRoot = [System.IO.Path]::GetFullPath($storeOutputRoot)
        if (-not $resolvedStoreOutputRoot.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean Store output outside the repo: $resolvedStoreOutputRoot"
        }

        Remove-Item -LiteralPath $resolvedStoreOutputRoot -Recurse -Force
    }

    Write-Host 'Building IX Tray Store package artifacts...'
    & pwsh (Join-Path $repoRoot 'Build\Build-Project.ps1') `
        -SkipWorkspaceBuild `
        -ToolsOnly `
        -Targets IntelligenceX.Tray `
        -Runtimes $Runtimes `
        -Frameworks net10.0-windows10.0.19041.0 `
        -Styles FrameworkDependent `
        -ToolOutputs Store
    if ($LASTEXITCODE -ne 0) {
        throw "IX Tray Store package build failed with exit code $LASTEXITCODE."
    }
}

if ($SubmissionMode -ne 'None') {
    if ($UseExampleSubmitConfig) {
        $SubmitConfigPath = 'Build/store.submit.tray.example.json'
    }

    $resolvedSubmitConfigPath = if ([System.IO.Path]::IsPathRooted($SubmitConfigPath)) {
        [System.IO.Path]::GetFullPath($SubmitConfigPath)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path $repoRoot $SubmitConfigPath))
    }

    if (-not (Test-Path -LiteralPath $resolvedSubmitConfigPath)) {
        throw "Store submit config not found: $resolvedSubmitConfigPath. Copy Build/store.submit.tray.example.json to Build/store.submit.tray.local.json and fill Partner Center values."
    }

    . (Join-Path $repoRoot 'Build\Internal\Resolve-PowerForgeCli.ps1')
    $cli = Resolve-PowerForgeCliInvocation -RepoRoot $repoRoot
    $submitArgs = @($cli.Prefix) + @(
        'store', 'submit',
        '--config', $resolvedSubmitConfigPath,
        '--target', 'IntelligenceX.Tray.Store'
    )

    if ($SubmissionMode -eq 'Plan') {
        $submitArgs += '--plan'
    } elseif ($SubmissionMode -eq 'Validate') {
        $submitArgs += '--validate'
    }

    Write-Host "Running Store submission $SubmissionMode for IX Tray..."
    & $cli.Command @submitArgs
    if ($LASTEXITCODE -ne 0) {
        throw "PowerForge Store submission $SubmissionMode failed with exit code $LASTEXITCODE."
    }
}
