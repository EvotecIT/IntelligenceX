param(
    [ValidateSet("fast", "full")]
    [string] $Mode = "fast"
)

$ErrorActionPreference = "Stop"

function Invoke-Checked([string[]] $Command) {
    & $Command[0] $Command[1..($Command.Length - 1)]
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $($Command -join ' ')"
    }
}

$repoRoot = (git rev-parse --show-toplevel).Trim()
Set-Location -LiteralPath $repoRoot

Invoke-Checked @("dotnet", "build", "IntelligenceX.sln", "-c", "Release")
Invoke-Checked @("dotnet", "test", "IntelligenceX.sln", "-c", "Release", "--no-build", "-v", "minimal")

if ($Mode -eq "full") {
    Invoke-Checked @("dotnet", "./IntelligenceX.Tests/bin/Release/net8.0/IntelligenceX.Tests.dll")
    Invoke-Checked @("dotnet", "./IntelligenceX.Tests/bin/Release/net10.0/IntelligenceX.Tests.dll")
}

Write-Host "OK: onboarding validation completed ($Mode)"
