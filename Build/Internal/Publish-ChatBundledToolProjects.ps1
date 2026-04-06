function Get-ChatBundledToolProjects {
    param(
        [switch] $IncludePrivateToolPacks
    )

    $bundledToolProjects = @(
        @{ Path = 'IntelligenceX.Tools\IntelligenceX.Tools.DnsClientX\IntelligenceX.Tools.DnsClientX.csproj'; Framework = 'net10.0' },
        @{ Path = 'IntelligenceX.Tools\IntelligenceX.Tools.DomainDetective\IntelligenceX.Tools.DomainDetective.csproj'; Framework = 'net10.0' },
        @{ Path = 'IntelligenceX.Tools\IntelligenceX.Tools.Email\IntelligenceX.Tools.Email.csproj'; Framework = 'net10.0' },
        @{ Path = 'IntelligenceX.Tools\IntelligenceX.Tools.EventLog\IntelligenceX.Tools.EventLog.csproj'; Framework = 'net10.0-windows' },
        @{ Path = 'IntelligenceX.Tools\IntelligenceX.Tools.FileSystem\IntelligenceX.Tools.FileSystem.csproj'; Framework = 'net10.0-windows' },
        @{ Path = 'IntelligenceX.Tools\IntelligenceX.Tools.OfficeIMO\IntelligenceX.Tools.OfficeIMO.csproj'; Framework = 'net10.0' },
        @{ Path = 'IntelligenceX.Tools\IntelligenceX.Tools.PowerShell\IntelligenceX.Tools.PowerShell.csproj'; Framework = 'net10.0-windows' },
        @{ Path = 'IntelligenceX.Tools\IntelligenceX.Tools.ReviewerSetup\IntelligenceX.Tools.ReviewerSetup.csproj'; Framework = 'net10.0-windows' }
    )

    if ($IncludePrivateToolPacks) {
        $bundledToolProjects += @(
            @{ Path = 'IntelligenceX.Tools\IntelligenceX.Tools.System\IntelligenceX.Tools.System.csproj'; Framework = 'net10.0-windows' },
            @{ Path = 'IntelligenceX.Tools\IntelligenceX.Tools.ADPlayground\IntelligenceX.Tools.ADPlayground.csproj'; Framework = 'net10.0-windows' },
            @{ Path = 'IntelligenceX.Tools\IntelligenceX.Tools.TestimoX\IntelligenceX.Tools.TestimoX.csproj'; Framework = 'net10.0-windows' },
            @{ Path = 'IntelligenceX.Tools\IntelligenceX.Tools.TestimoX.Analytics\IntelligenceX.Tools.TestimoX.Analytics.csproj'; Framework = 'net10.0-windows' }
        )
    }

    return $bundledToolProjects
}

function Publish-ChatBundledToolProjects {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [Parameter(Mandatory)]
        [string] $OutputPath,
        [Parameter(Mandatory)]
        [string] $Configuration,
        [Parameter(Mandatory)]
        [string] $Runtime,
        [switch] $NoBuild,
        [switch] $IncludePrivateToolPacks,
        [string] $TestimoXRoot
    )

    $outputPathFull = [System.IO.Path]::GetFullPath($OutputPath)
    if (-not (Test-Path $outputPathFull)) {
        New-Item -ItemType Directory -Path $outputPathFull -Force | Out-Null
    }

    foreach ($toolProject in (Get-ChatBundledToolProjects -IncludePrivateToolPacks:$IncludePrivateToolPacks)) {
        $toolProjectPath = Join-Path $RepoRoot $toolProject.Path
        $publishArgs = @(
            'publish',
            $toolProjectPath,
            '-c',
            $Configuration,
            '-f',
            $toolProject.Framework,
            '-r',
            $Runtime,
            '-o',
            $outputPathFull,
            '--no-self-contained'
        )
        if ($NoBuild) {
            $publishArgs += '--no-build'
        }
        if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
            $resolved = [System.IO.Path]::GetFullPath($TestimoXRoot)
            if (-not $resolved.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
                $resolved += [System.IO.Path]::DirectorySeparatorChar
            }
            $publishArgs += "/p:TestimoXRoot=$resolved"
        }

        Write-Step "Bundle tool project: $toolProjectPath"
        Push-Location $RepoRoot
        try {
            & dotnet @publishArgs
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet command failed with exit code ${LASTEXITCODE}: dotnet $($publishArgs -join ' ')"
            }
        } finally {
            Pop-Location
        }
    }
}
