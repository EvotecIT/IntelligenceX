function Assert-ChatHostArtifacts {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [switch] $IncludePrivateToolPacks
    )

    $rootPathFull = [System.IO.Path]::GetFullPath($RootPath)
    if (-not (Test-Path $rootPathFull)) {
        throw "Expected chat host output directory does not exist: $rootPathFull"
    }

    function Assert-ArtifactPresent {
        param([Parameter(Mandatory)][string] $Path)

        if (-not (Test-Path $Path)) {
            throw "Expected chat host artifact missing: $Path"
        }
    }

    Assert-ArtifactPresent (Join-Path $rootPathFull 'IntelligenceX.Chat.Host.exe')
    Assert-ArtifactPresent (Join-Path $rootPathFull 'IntelligenceX.Tools.DnsClientX.dll')
    Assert-ArtifactPresent (Join-Path $rootPathFull 'IntelligenceX.Tools.DomainDetective.dll')
    Assert-ArtifactPresent (Join-Path $rootPathFull 'IntelligenceX.Tools.Email.dll')
    Assert-ArtifactPresent (Join-Path $rootPathFull 'IntelligenceX.Tools.EventLog.dll')
    Assert-ArtifactPresent (Join-Path $rootPathFull 'IntelligenceX.Tools.FileSystem.dll')
    Assert-ArtifactPresent (Join-Path $rootPathFull 'IntelligenceX.Tools.OfficeIMO.dll')
    Assert-ArtifactPresent (Join-Path $rootPathFull 'IntelligenceX.Tools.PowerShell.dll')
    Assert-ArtifactPresent (Join-Path $rootPathFull 'IntelligenceX.Tools.ReviewerSetup.dll')
    Assert-ArtifactPresent (Join-Path $rootPathFull 'System.Diagnostics.EventLog.dll')

    if (-not $IncludePrivateToolPacks) {
        return
    }

    Assert-ArtifactPresent (Join-Path $rootPathFull 'IntelligenceX.Tools.System.dll')
    Assert-ArtifactPresent (Join-Path $rootPathFull 'IntelligenceX.Tools.ADPlayground.dll')
    Assert-ArtifactPresent (Join-Path $rootPathFull 'IntelligenceX.Tools.TestimoX.dll')
    Assert-ArtifactPresent (Join-Path $rootPathFull 'IntelligenceX.Tools.TestimoX.Analytics.dll')
    Assert-ArtifactPresent (Join-Path $rootPathFull 'ADPlayground.Monitoring.dll')
}
