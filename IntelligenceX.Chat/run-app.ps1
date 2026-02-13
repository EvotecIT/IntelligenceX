param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'

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

$appProject = Join-Path $PSScriptRoot 'IntelligenceX.Chat.App\IntelligenceX.Chat.App.csproj'

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

Write-Host "Starting IntelligenceX.Chat.App ($Configuration) from $appProject" -ForegroundColor Cyan
& dotnet @dotnetArgs
exit $LASTEXITCODE
