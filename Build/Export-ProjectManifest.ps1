[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string] $OutputPath = 'WebsiteArtifacts/project-manifest.json'
)

function Get-GitValue {
    param(
        [Parameter(Mandatory)][string] $RepoPath,
        [Parameter(Mandatory)][string[]] $Arguments
    )

    try {
        $Value = & git -C $RepoPath @Arguments 2>$null
        if ($LASTEXITCODE -ne 0) {
            return $null
        }
        return ($Value | Select-Object -First 1)
    } catch {
        return $null
    }
}

function Resolve-Version {
    param(
        [Parameter(Mandatory)][string] $RepoPath
    )

    $Tag = Get-GitValue -RepoPath $RepoPath -Arguments @('describe', '--tags', '--abbrev=0')
    if ($Tag) {
        $Normalized = [string]$Tag
        $Normalized = $Normalized -replace '^IntelligenceX-v', ''
        $Normalized = $Normalized -replace '^v', ''
        if ($Normalized) {
            return $Normalized
        }
    }

    $ProjectPath = Join-Path $RepoPath 'IntelligenceX/IntelligenceX.csproj'
    if (Test-Path -LiteralPath $ProjectPath) {
        $VersionPrefix = Select-String -Path $ProjectPath -Pattern '<VersionPrefix>([^<]+)</VersionPrefix>' -AllMatches | Select-Object -First 1
        if ($VersionPrefix -and $VersionPrefix.Matches.Count -gt 0) {
            return $VersionPrefix.Matches[0].Groups[1].Value
        }
    }

    return '0.1.0'
}

$RepoRootResolved = [System.IO.Path]::GetFullPath($RepoRoot)
$OutputPathResolved = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    [System.IO.Path]::GetFullPath($OutputPath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $RepoRootResolved $OutputPath))
}
$OutputDirectory = Split-Path -Path $OutputPathResolved -Parent
if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    $null = New-Item -Path $OutputDirectory -ItemType Directory -Force
}

$Version = Resolve-Version -RepoPath $RepoRootResolved
$Commit = Get-GitValue -RepoPath $RepoRootResolved -Arguments @('rev-parse', '--short', 'HEAD')
$GeneratedAt = Get-GitValue -RepoPath $RepoRootResolved -Arguments @('show', '-s', '--format=%cI', 'HEAD')
if (-not $GeneratedAt) {
    $GeneratedAt = (Get-Date).ToUniversalTime().ToString('o')
}

$Manifest = [ordered]@{
    slug        = 'intelligencex'
    name        = 'IntelligenceX'
    mode        = 'dedicated-external'
    version     = $Version
    generatedAt = [string]$GeneratedAt
    commit      = [string]$Commit
    description = 'AI-powered operations and documentation assistant.'
    surfaces    = [ordered]@{
        docs          = $true
        apiDotNet     = $true
        apiPowerShell = $true
        changelog     = $true
        releases      = $true
    }
    links       = [ordered]@{
        website       = 'https://intelligencex.app'
        docs          = 'https://intelligencex.dev/docs/'
        apiDotNet     = 'https://intelligencex.dev/api/'
        apiPowerShell = 'https://intelligencex.dev/api/powershell/'
        changelog     = 'https://intelligencex.dev/changelog/'
        releases      = 'https://github.com/EvotecIT/IntelligenceX/releases'
        source        = 'https://github.com/EvotecIT/IntelligenceX'
    }
}

if ($PSCmdlet.ShouldProcess($OutputPathResolved, 'Write project-manifest.json')) {
    $Manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $OutputPathResolved -Encoding utf8
}

[PSCustomObject]@{
    outputPath = $OutputPathResolved
    version    = $Version
    commit     = $Commit
}
