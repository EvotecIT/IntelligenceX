using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Analysis;
using IntelligenceX.Json;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    private static string BuildPowerShellRunnerScript() {
        return @"param(
    [Parameter(Mandatory=$true)][string]$Workspace,
    [Parameter(Mandatory=$true)][string]$OutFile,
    [Parameter()][string]$SettingsPath,
    [Parameter()][string]$ExcludedDirectoriesCsv,
    [Parameter()][switch]$FailOnAnalyzerErrors
)
$ErrorActionPreference = 'Stop'

if ([System.IO.File]::Exists($Workspace)) {
    throw ('Workspace path is not a directory: ' + $Workspace)
}
if (-not [System.IO.Directory]::Exists($Workspace)) {
    throw ('Workspace path not found: ' + $Workspace)
}
$workspaceRoot = [System.IO.Path]::GetFullPath($Workspace)

if (-not (Get-Module -ListAvailable -Name PSScriptAnalyzer)) {
    throw 'PSScriptAnalyzer module not found. Install with: Install-Module PSScriptAnalyzer -Scope CurrentUser'
}
Import-Module PSScriptAnalyzer -ErrorAction Stop

$excludedSegmentSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
if ($ExcludedDirectoriesCsv) {
    foreach ($segment in $ExcludedDirectoriesCsv.Split(',')) {
        if ([string]::IsNullOrWhiteSpace($segment)) {
            continue
        }
        [void]$excludedSegmentSet.Add($segment.Trim())
    }
}

function Get-AnalyzerPaths {
    param(
        [Parameter(Mandatory=$true)][string]$Root,
        [Parameter(Mandatory=$true)][System.Collections.Generic.HashSet[string]]$ExcludedSegments
    )

    $paths = New-Object System.Collections.Generic.List[string]
    $stack = New-Object System.Collections.Generic.Stack[string]
    $stack.Push([System.IO.Path]::GetFullPath($Root))

    while ($stack.Count -gt 0) {
        $current = $stack.Pop()

        try {
            foreach ($subdirectory in [System.IO.Directory]::EnumerateDirectories($current)) {
                $name = [System.IO.Path]::GetFileName($subdirectory)
                if (-not [string]::IsNullOrWhiteSpace($name) -and $ExcludedSegments.Contains($name)) {
                    continue
                }

                try {
                    $attributes = [System.IO.File]::GetAttributes($subdirectory)
                    if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                        continue
                    }
                } catch [System.UnauthorizedAccessException] {
                    continue
                } catch [System.IO.PathTooLongException] {
                    continue
                } catch [System.IO.DirectoryNotFoundException] {
                    continue
                } catch [System.IO.IOException] {
                    continue
                }

                $stack.Push($subdirectory)
            }
        } catch [System.UnauthorizedAccessException] {
            continue
        } catch [System.IO.PathTooLongException] {
            continue
        } catch [System.IO.DirectoryNotFoundException] {
            continue
        } catch [System.IO.IOException] {
            continue
        }

        try {
            foreach ($file in [System.IO.Directory]::EnumerateFiles($current)) {
                try {
                    $attributes = [System.IO.File]::GetAttributes($file)
                    if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                        continue
                    }
                } catch [System.UnauthorizedAccessException] {
                    continue
                } catch [System.IO.PathTooLongException] {
                    continue
                } catch [System.IO.DirectoryNotFoundException] {
                    continue
                } catch [System.IO.IOException] {
                    continue
                }

                $extension = [System.IO.Path]::GetExtension($file)
                if ([string]::Equals($extension, '.ps1', [System.StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals($extension, '.psm1', [System.StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals($extension, '.psd1', [System.StringComparison]::OrdinalIgnoreCase)) {
                    [void]$paths.Add($file)
                }
            }
        } catch [System.UnauthorizedAccessException] {
            continue
        } catch [System.IO.PathTooLongException] {
            continue
        } catch [System.IO.DirectoryNotFoundException] {
            continue
        } catch [System.IO.IOException] {
            continue
        }
    }

    return $paths.ToArray()
}

$analysisPaths = Get-AnalyzerPaths -Root $workspaceRoot -ExcludedSegments $excludedSegmentSet
$invokeSeverity = @('Error','Warning','Information')
$hasSettings = $SettingsPath -and (Test-Path -LiteralPath $SettingsPath)

$invokeErrors = @()
$results = New-Object System.Collections.Generic.List[object]
if ($analysisPaths.Length -gt 0) {
    foreach ($analysisPath in $analysisPaths) {
        if ([string]::IsNullOrWhiteSpace($analysisPath)) {
            continue
        }

        try {
            $localErrors = @()
            $localResults = if ($hasSettings) {
                @(Invoke-ScriptAnalyzer -Path $analysisPath -Severity $invokeSeverity -Settings $SettingsPath -ErrorAction Continue -ErrorVariable +localErrors)
            } else {
                @(Invoke-ScriptAnalyzer -Path $analysisPath -Severity $invokeSeverity -ErrorAction Continue -ErrorVariable +localErrors)
            }

            # Work around intermittent PSScriptAnalyzer engine crashes (NullReferenceException) by retrying once.
            $shouldRetry = $false
            foreach ($err in $localErrors) {
                $msg = if ($err.Exception -and $err.Exception.Message) { $err.Exception.Message } else { [string]$err }
                if ($msg -like '*Object reference not set*' -or $msg -like '*NullReferenceException*') {
                    $shouldRetry = $true
                    break
                }
            }

            if ($shouldRetry) {
                Import-Module PSScriptAnalyzer -Force -ErrorAction SilentlyContinue | Out-Null

                $retryErrors = @()
                $retryResults = if ($hasSettings) {
                    @(Invoke-ScriptAnalyzer -Path $analysisPath -Severity $invokeSeverity -Settings $SettingsPath -ErrorAction Continue -ErrorVariable +retryErrors)
                } else {
                    @(Invoke-ScriptAnalyzer -Path $analysisPath -Severity $invokeSeverity -ErrorAction Continue -ErrorVariable +retryErrors)
                }

                if ($retryErrors.Count -eq 0) {
                    foreach ($result in $retryResults) {
                        [void]$results.Add($result)
                    }
                    continue
                }

                foreach ($result in $localResults) {
                    [void]$results.Add($result)
                }

                foreach ($err in $localErrors) {
                    $invokeErrors += $err
                }
                foreach ($err in $retryErrors) {
                    $invokeErrors += $err
                }

                continue
            }

            foreach ($result in $localResults) {
                [void]$results.Add($result)
            }
            foreach ($err in $localErrors) {
                $invokeErrors += $err
            }
        } catch {
            $invokeErrors += $_
        }
    }
}

$sawInvokeErrors = $false
foreach ($invokeError in $invokeErrors) {
    $sawInvokeErrors = $true
    $errorText = if ($invokeError.Exception -and $invokeError.Exception.Message) {
        $invokeError.Exception.Message
    } else {
        [string]$invokeError
    }
    [Console]::Error.WriteLine('PSScriptAnalyzer engine error: ' + $errorText)
}

$items = @()
foreach ($result in $results) {
    if (-not $result.ScriptPath -or -not $result.Message) {
        continue
    }
    $severity = switch ($result.Severity) {
        'Error' { 'error' }
        'Warning' { 'warning' }
        default { 'info' }
    }
    $items += [ordered]@{
        path = [string]$result.ScriptPath
        line = [int]($result.Line)
        severity = $severity
        message = [string]$result.Message
        ruleId = [string]$result.RuleName
        tool = 'PSScriptAnalyzer'
    }
}

$directory = [System.IO.Path]::GetDirectoryName($OutFile)
if ($directory -and -not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

[ordered]@{
    schema = 'intelligencex.findings.v1'
    items = $items
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutFile -Encoding UTF8

Write-Output ('PSScriptAnalyzer findings: ' + $items.Count)
if ($sawInvokeErrors -and $FailOnAnalyzerErrors) {
    [Console]::Error.WriteLine('PSScriptAnalyzer reported one or more engine errors.')
    exit 2
}";
    }
}
