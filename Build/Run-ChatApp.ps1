# One-command local WinUI chat app startup (build-root entrypoint).

[CmdletBinding()] param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $NoBuild,
    [switch] $IncludePrivateToolPacks,
    [string] $TestimoXRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Step($text)   { Write-Host "[+] $text" -ForegroundColor Yellow }

function Stop-IfRunning {
    param([string[]] $Names)

    foreach ($name in $Names) {
        $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
        if (-not $procs) {
            continue
        }

        foreach ($p in $procs) {
            try {
                Stop-Process -Id $p.Id -Force -ErrorAction Stop
            } catch {
                Write-Warning "Could not stop process '$name' (pid $($p.Id)): $($_.Exception.Message)"
            }
        }
    }
}

$repoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName
$appProject = Join-Path $repoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.App\IntelligenceX.Chat.App.csproj'

if (-not (Test-Path $appProject)) {
    throw "Project not found: $appProject"
}

# Prevent file lock issues during build/run.
Stop-IfRunning -Names @('IntelligenceX.Chat.App', 'IntelligenceX.Chat.Service')

$dotnetArgs = @(
    'run',
    '--project', $appProject,
    '-c', $Configuration
)

if ($NoBuild) {
    $dotnetArgs += '--no-build'
}
if ($IncludePrivateToolPacks) {
    $dotnetArgs += '/p:IncludePrivateToolPacks=true'
    if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
        $resolved = [System.IO.Path]::GetFullPath($TestimoXRoot)
        if (-not $resolved.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
            $resolved += [System.IO.Path]::DirectorySeparatorChar
        }
        $dotnetArgs += "/p:TestimoXRoot=$resolved"
    }
}

Write-Header 'Run Chat App'
Write-Step "Configuration: $Configuration"
Write-Step "Project: $appProject"

Push-Location $repoRoot
try {
    & dotnet @dotnetArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet run failed with exit code $LASTEXITCODE."
    }
} finally {
    Pop-Location
}
