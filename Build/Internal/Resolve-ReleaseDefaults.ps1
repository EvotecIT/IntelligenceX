function Resolve-TestimoXDefaultSignThumbprint {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot
    )

    $candidates = @(
        (Join-Path $RepoRoot '..\TestimoX\Build\Build-TestimoX.Agent-MSI.ps1'),
        (Join-Path $RepoRoot '..\TestimoX\Build\Prepare-TestimoX.Agent-MSI.ps1'),
        (Join-Path $RepoRoot '..\TestimoX\Build\Deploy-TestimoX.Agent.ps1')
    )

    foreach ($candidate in $candidates) {
        if (-not (Test-Path $candidate)) {
            continue
        }

        try {
            $raw = Get-Content -Path $candidate -Raw -ErrorAction Stop
            $match = [System.Text.RegularExpressions.Regex]::Match(
                $raw,
                '(?im)^\s*\[string\]\s*\$SignThumbprint\s*=\s*''(?<thumb>[0-9a-f]{40})''')
            if ($match.Success) {
                return $match.Groups['thumb'].Value.ToLowerInvariant()
            }
        } catch {
        }
    }

    return $null
}

function Resolve-DefaultSignThumbprint {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [string] $ExplicitThumbprint,
        [bool] $UseTestimoXFallback = $true
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitThumbprint)) {
        return $ExplicitThumbprint.Trim()
    }

    foreach ($envName in @(
        'CERT_THUMBPRINT',
        'SIGN_THUMBPRINT',
        'CODE_SIGN_THUMBPRINT',
        'INTELLIGENCEX_SIGN_THUMBPRINT',
        'TESTIMOX_SIGN_THUMBPRINT'
    )) {
        $value = [Environment]::GetEnvironmentVariable($envName)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }
    }

    if ($UseTestimoXFallback) {
        return Resolve-TestimoXDefaultSignThumbprint -RepoRoot $RepoRoot
    }

    return $null
}

function Resolve-PrimaryExecutableName {
    param([Parameter(Mandatory)][string] $FrontendName)

    if ($FrontendName -eq 'app') {
        return 'IntelligenceX.Chat.App.exe'
    }

    return 'IntelligenceX.Chat.Host.exe'
}
