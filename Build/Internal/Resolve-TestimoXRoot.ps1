function Ensure-TestimoXTrailingSlash {
    param([Parameter(Mandatory)][string] $Path)

    $full = [System.IO.Path]::GetFullPath($Path)
    if ($full.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        return $full
    }

    return ($full + [System.IO.Path]::DirectorySeparatorChar)
}

function Get-RepoSiblingRootCandidates {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot
    )

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($candidate in @(
        (Join-Path $RepoRoot '..'),
        (Join-Path $RepoRoot '..\..'),
        (Join-Path $RepoRoot '..\..\..'),
        (Join-Path $RepoRoot '..\..\..\..')
    )) {
        $full = [System.IO.Path]::GetFullPath($candidate)
        if ($seen.Add($full)) {
            $full
        }
    }
}

function Test-RepoMarkers {
    param(
        [Parameter(Mandatory)]
        [string] $Root,
        [Parameter(Mandatory)]
        [string[]] $MarkerRelativePaths
    )

    $full = [System.IO.Path]::GetFullPath($Root)
    foreach ($relativeMarker in $MarkerRelativePaths) {
        if (-not (Test-Path (Join-Path $full $relativeMarker))) {
            return $false
        }
    }

    return $true
}

function Resolve-OptionalSiblingRepoRoot {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [Parameter(Mandatory)]
        [string[]] $RepoNames,
        [Parameter(Mandatory)]
        [string[]] $MarkerRelativePaths
    )

    foreach ($baseRoot in (Get-RepoSiblingRootCandidates -RepoRoot $RepoRoot)) {
        foreach ($repoName in $RepoNames) {
            $candidate = Join-Path $baseRoot $repoName
            if (Test-RepoMarkers -Root $candidate -MarkerRelativePaths $MarkerRelativePaths) {
                return (Ensure-TestimoXTrailingSlash -Path $candidate)
            }
        }
    }

    return $null
}

function Get-TestimoXRootCandidates {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot
    )

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($baseRoot in (Get-RepoSiblingRootCandidates -RepoRoot $RepoRoot)) {
        foreach ($repoName in @('TestimoX', 'TestimoX-master')) {
            $candidate = [System.IO.Path]::GetFullPath((Join-Path $baseRoot $repoName))
            if ($seen.Add($candidate)) {
                $candidate
            }
        }
    }
}

function Test-TestimoXMarkers {
    param([Parameter(Mandatory)][string] $Root)

    return (Test-RepoMarkers -Root $Root -MarkerRelativePaths @(
        'ADPlayground\ADPlayground.csproj',
        'ComputerX\Features\FeatureInventoryQuery.cs',
        'ComputerX\PowerShellRuntime\PowerShellCommandQuery.cs'
    ))
}

function Resolve-TestimoXRoot {
    param(
        [string] $Provided,
        [Parameter(Mandatory)]
        [string] $RepoRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($Provided)) {
        if (-not (Test-TestimoXMarkers -Root $Provided)) {
            throw "Provided -TestimoXRoot does not contain required markers: $Provided"
        }

        return (Ensure-TestimoXTrailingSlash -Path $Provided)
    }

    foreach ($envName in @('TESTIMOX_ROOT', 'TestimoXRoot')) {
        $fromEnvironment = [System.Environment]::GetEnvironmentVariable($envName)
        if ([string]::IsNullOrWhiteSpace($fromEnvironment)) {
            continue
        }

        if (-not (Test-TestimoXMarkers -Root $fromEnvironment)) {
            throw "Environment variable $envName does not contain required TestimoX markers: $fromEnvironment"
        }

        return (Ensure-TestimoXTrailingSlash -Path $fromEnvironment)
    }

    foreach ($candidate in (Get-TestimoXRootCandidates -RepoRoot $RepoRoot)) {
        if (Test-TestimoXMarkers -Root $candidate) {
            return (Ensure-TestimoXTrailingSlash -Path $candidate)
        }
    }

    throw "Unable to locate TestimoX private engines. Pass -TestimoXRoot explicitly or set TESTIMOX_ROOT."
}
