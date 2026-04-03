function Resolve-PowerForgeCliInvocation {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_CLI_PATH)) {
        $configuredCliPath = $env:POWERFORGE_CLI_PATH
        if ([System.IO.Path]::IsPathRooted($configuredCliPath)) {
            $explicitCliPath = [System.IO.Path]::GetFullPath($configuredCliPath)
        } else {
            $explicitCliPath = [System.IO.Path]::GetFullPath(
                [System.IO.Path]::Combine($RepoRoot, $configuredCliPath)
            )
        }
        if (-not (Test-Path -LiteralPath $explicitCliPath)) {
            throw "POWERFORGE_CLI_PATH does not exist: $explicitCliPath"
        }

        $extension = [System.IO.Path]::GetExtension($explicitCliPath)
        if ($extension -and $extension.Equals('.dll', [System.StringComparison]::OrdinalIgnoreCase)) {
            return @{
                Command = 'dotnet'
                Prefix = @($explicitCliPath)
                Source = $explicitCliPath
            }
        }

        if ($extension -and $extension.Equals('.ps1', [System.StringComparison]::OrdinalIgnoreCase)) {
            return @{
                Command = 'pwsh'
                Prefix = @('-NoProfile', '-File', $explicitCliPath)
                Source = $explicitCliPath
            }
        }

        return @{
            Command = $explicitCliPath
            Prefix = @()
            Source = $explicitCliPath
        }
    }

    function Get-LatestCliSourceWriteTimeUtc {
        param(
            [Parameter(Mandatory)]
            [string] $RootPath
        )

        $sourceRoots = @(
            (Join-Path $RootPath 'PowerForge'),
            (Join-Path $RootPath 'PowerForge.Cli'),
            (Join-Path $RootPath 'PowerForge.PowerShell')
        ) | Where-Object { Test-Path -LiteralPath $_ }

        $latest = [datetime]::MinValue
        foreach ($sourceRoot in $sourceRoots) {
            $items = Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.Extension -in @('.cs', '.csproj', '.props', '.targets', '.json')
                }

            foreach ($item in $items) {
                if ($item.LastWriteTimeUtc -gt $latest) {
                    $latest = $item.LastWriteTimeUtc
                }
            }
        }

        return $latest
    }

    function Get-BuiltCliInvocation {
        param(
            [Parameter(Mandatory)]
            [string] $RootPath
        )

        $dllCandidates = @(
            (Join-Path $RootPath 'PowerForge.Cli\bin\Release\net10.0\PowerForge.Cli.dll'),
            (Join-Path $RootPath 'PowerForge.Cli\bin\Release\net8.0\PowerForge.Cli.dll')
        )

        foreach ($candidate in $dllCandidates) {
            if (Test-Path -LiteralPath $candidate) {
                return @{
                    Command = 'dotnet'
                    Prefix = @($candidate)
                    Source = $candidate
                    LastWriteTimeUtc = (Get-Item -LiteralPath $candidate).LastWriteTimeUtc
                }
            }
        }

        $exeCandidates = @(
            (Join-Path $RootPath 'PowerForge.Cli\bin\Release\net10.0\PowerForge.Cli.exe'),
            (Join-Path $RootPath 'PowerForge.Cli\bin\Release\net8.0\PowerForge.Cli.exe')
        )

        foreach ($candidate in $exeCandidates) {
            if (Test-Path -LiteralPath $candidate) {
                return @{
                    Command = $candidate
                    Prefix = @()
                    Source = $candidate
                    LastWriteTimeUtc = (Get-Item -LiteralPath $candidate).LastWriteTimeUtc
                }
            }
        }

        return $null
    }

    function New-CliInvocationFromRoot {
        param(
            [Parameter(Mandatory)]
            [string] $RootPath
        )

        $fullRoot = [System.IO.Path]::GetFullPath($RootPath)
        $projectPath = Join-Path $fullRoot 'PowerForge.Cli\PowerForge.Cli.csproj'
        $forceBuiltCli = [Environment]::GetEnvironmentVariable('POWERFORGE_USE_BUILT_CLI')
        $builtCli = Get-BuiltCliInvocation -RootPath $fullRoot

        if ($builtCli -and $forceBuiltCli -and $forceBuiltCli.Equals('true', [System.StringComparison]::OrdinalIgnoreCase)) {
            $builtCli.Remove('LastWriteTimeUtc')
            return $builtCli
        }

        if ($builtCli) {
            $latestSourceWriteTimeUtc = Get-LatestCliSourceWriteTimeUtc -RootPath $fullRoot
            if ($builtCli.LastWriteTimeUtc -ge $latestSourceWriteTimeUtc) {
                $builtCli.Remove('LastWriteTimeUtc')
                return $builtCli
            }
        }

        if (Test-Path -LiteralPath $projectPath) {
            return @{
                Command = 'dotnet'
                Prefix = @('run', '--project', $projectPath, '-c', 'Release', '--framework', 'net10.0', '--')
                Source = $projectPath
            }
        }

        if ($builtCli) {
            $builtCli.Remove('LastWriteTimeUtc')
            return $builtCli
        }

        return $null
    }

    $roots = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($env:POWERFORGE_ROOT)) {
        $roots.Add($env:POWERFORGE_ROOT)
    }

    $roots.Add((Join-Path $RepoRoot '..\PSPublishModule'))
    $roots.Add((Join-Path $RepoRoot '..\..\PSPublishModule'))

    foreach ($root in ($roots | Select-Object -Unique)) {
        $invocation = New-CliInvocationFromRoot -RootPath $root
        if ($null -ne $invocation) {
            return $invocation
        }
    }

    $installed = Get-Command powerforge -ErrorAction SilentlyContinue
    if ($installed) {
        return @{
            Command = $installed.Source
            Prefix = @()
        }
    }

    throw "Unable to resolve PowerForge CLI. Install 'powerforge' or set POWERFORGE_ROOT to the PSPublishModule repo root."
}
