function Ensure-TestimoXTrailingSlash {
    param([Parameter(Mandatory)][string] $Path)

    $full = [System.IO.Path]::GetFullPath($Path)
    if ($full.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        return $full
    }

    return ($full + [System.IO.Path]::DirectorySeparatorChar)
}

function Get-TestimoXRootCandidates {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot
    )

    return @(
        (Join-Path $RepoRoot '..\TestimoX'),
        (Join-Path $RepoRoot '..\TestimoX-master'),
        (Join-Path $RepoRoot '..\..\TestimoX'),
        (Join-Path $RepoRoot '..\..\TestimoX-master')
    )
}

function Test-TestimoXMarkers {
    param([Parameter(Mandatory)][string] $Root)

    $full = [System.IO.Path]::GetFullPath($Root)
    $markers = @(
        (Join-Path $full 'ADPlayground\ADPlayground.csproj'),
        (Join-Path $full 'ComputerX\Features\FeatureInventoryQuery.cs'),
        (Join-Path $full 'ComputerX\PowerShellRuntime\PowerShellCommandQuery.cs')
    )

    foreach ($marker in $markers) {
        if (-not (Test-Path $marker)) {
            return $false
        }
    }

    return $true
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
