param(
    [Parameter(Mandatory = $true)]
    [string] $PrNumber
)

$ErrorActionPreference = "Stop"
gh pr checks $PrNumber --repo EvotecIT/IntelligenceX --watch --interval 10
