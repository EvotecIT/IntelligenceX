param(
    [ValidateSet("fast", "full")]
    [string] $Mode = "fast"
)

$ErrorActionPreference = "Stop"

function Find-RepoRoot {
    $dir = (Get-Location).Path
    while ($true) {
        if (Test-Path -LiteralPath (Join-Path $dir "IntelligenceX.sln")) {
            return $dir
        }

        $parent = Split-Path -LiteralPath $dir -Parent
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $dir) {
            return $null
        }
        $dir = $parent
    }
}

function Invoke-Checked([string[]] $Command) {
    & $Command[0] $Command[1..($Command.Length - 1)]
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $($Command -join ' ')"
    }
}

$repoRoot = $env:REPO_ROOT
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    $repoRoot = Find-RepoRoot
}
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    Write-Error "ERROR: could not locate repo root (expected to find IntelligenceX.sln)"
    exit 1
}

Set-Location -LiteralPath $repoRoot

function Run-Fast {
    Invoke-Checked @("dotnet", "run", "--project", "IntelligenceX.Cli/IntelligenceX.Cli.csproj", "--framework", "net8.0", "--", "analyze", "validate-catalog", "--workspace", ".")
    Invoke-Checked @("dotnet", "run", "--project", "IntelligenceX.Cli/IntelligenceX.Cli.csproj", "--framework", "net8.0", "--", "analyze", "run", "--config", ".intelligencex/reviewer.json", "--out", "artifacts", "--framework", "net8.0")
}

if ($Mode -eq "full") {
    Invoke-Checked @("dotnet", "build", "IntelligenceX.sln", "-c", "Release")
    Invoke-Checked @("dotnet", "test", "IntelligenceX.sln", "-c", "Release", "--no-build", "-v", "minimal")
    Invoke-Checked @("dotnet", "./IntelligenceX.Tests/bin/Release/net8.0/IntelligenceX.Tests.dll")
    Invoke-Checked @("dotnet", "./IntelligenceX.Tests/bin/Release/net10.0/IntelligenceX.Tests.dll")
}

Run-Fast
Write-Host "OK: analysis suite completed ($Mode)"
