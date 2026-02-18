param()

$ErrorActionPreference = "Stop"

function Invoke-Checked([string[]] $Command) {
    & $Command[0] $Command[1..($Command.Length - 1)]
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $($Command -join ' ')"
    }
}

function Resolve-RepoRoot {
    try {
        return (git rev-parse --show-toplevel).Trim()
    } catch {
        throw "ERROR: unable to resolve git repo root."
    }
}

function Normalize-Language([string] $Language) {
    switch ($Language.ToLowerInvariant()) {
        "csharp" { return @{ Name = "csharp"; Ext = ".cs" } }
        "cs" { return @{ Name = "csharp"; Ext = ".cs" } }
        "powershell" { return @{ Name = "powershell"; Ext = ".ps1" } }
        "ps" { return @{ Name = "powershell"; Ext = ".ps1" } }
        "javascript" { return @{ Name = "javascript"; Ext = ".js" } }
        "js" { return @{ Name = "javascript"; Ext = ".js" } }
        "typescript" { return @{ Name = "typescript"; Ext = ".ts" } }
        "ts" { return @{ Name = "typescript"; Ext = ".ts" } }
        "python" { return @{ Name = "python"; Ext = ".py" } }
        "py" { return @{ Name = "python"; Ext = ".py" } }
        default {
            throw "ERROR: unsupported LANGUAGE '$Language'. Use csharp|powershell|javascript|typescript|python."
        }
    }
}

function New-SourceFile([string] $Path, [string] $LanguageName, [int] $Index, [int] $Lines) {
    $builder = [System.Text.StringBuilder]::new()
    switch ($LanguageName) {
        "csharp" {
            [void] $builder.AppendLine("namespace Bench;")
            [void] $builder.AppendLine("public class C$Index {")
            [void] $builder.AppendLine("    public int Sum$Index(int input) {")
            [void] $builder.AppendLine("        var total = 0;")
            [void] $builder.AppendLine("        total += input;")
            for ($j = 1; $j -le $Lines; $j++) {
                [void] $builder.AppendLine("        total += $j;")
            }
            [void] $builder.AppendLine("        return total;")
            [void] $builder.AppendLine("    }")
            [void] $builder.AppendLine("}")
        }
        "powershell" {
            [void] $builder.AppendLine("function Get-Sum$Index {")
            [void] $builder.AppendLine("    param([int]`$InputValue)")
            [void] $builder.AppendLine("    `$total = 0")
            [void] $builder.AppendLine("    `$total += `$InputValue")
            for ($j = 1; $j -le $Lines; $j++) {
                [void] $builder.AppendLine("    `$total += $j")
            }
            [void] $builder.AppendLine("    return `$total")
            [void] $builder.AppendLine("}")
        }
        "javascript" {
            [void] $builder.AppendLine("export function sum$Index(inputValue) {")
            [void] $builder.AppendLine("  let total = 0;")
            [void] $builder.AppendLine("  total += inputValue;")
            for ($j = 1; $j -le $Lines; $j++) {
                [void] $builder.AppendLine("  total += $j;")
            }
            [void] $builder.AppendLine("  return total;")
            [void] $builder.AppendLine("}")
        }
        "typescript" {
            [void] $builder.AppendLine("export function sum$Index(inputValue: number): number {")
            [void] $builder.AppendLine("  let total = 0;")
            [void] $builder.AppendLine("  total += inputValue;")
            for ($j = 1; $j -le $Lines; $j++) {
                [void] $builder.AppendLine("  total += $j;")
            }
            [void] $builder.AppendLine("  return total;")
            [void] $builder.AppendLine("}")
        }
        "python" {
            [void] $builder.AppendLine("def sum_$Index(input_value):")
            [void] $builder.AppendLine("    total = 0")
            [void] $builder.AppendLine("    total += input_value")
            for ($j = 1; $j -le $Lines; $j++) {
                [void] $builder.AppendLine("    total += $j")
            }
            [void] $builder.AppendLine("    return total")
        }
        default {
            throw "ERROR: unsupported language template '$LanguageName'."
        }
    }
    Set-Content -LiteralPath $Path -Value $builder.ToString() -Encoding UTF8
}

$repoRoot = Resolve-RepoRoot
Set-Location -LiteralPath $repoRoot

$files = [int]($env:FILES ?? "200")
$lines = [int]($env:LINES ?? "120")
$language = $env:LANGUAGE
if ([string]::IsNullOrWhiteSpace($language)) {
    $language = "csharp"
}
$framework = $env:FRAMEWORK
if ([string]::IsNullOrWhiteSpace($framework)) {
    $framework = "net8.0"
}
$keepWorkDir = ("" + ($env:KEEP_WORKDIR ?? "0")).Trim() -eq "1"
$workDir = ("" + $env:WORKDIR).Trim()
if ([string]::IsNullOrWhiteSpace($workDir)) {
    $workDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ix-dup-bench-" + [Guid]::NewGuid().ToString("N"))
}

$languageInfo = Normalize-Language $language
$languageName = [string]$languageInfo.Name
$ext = [string]$languageInfo.Ext
$extWithoutDot = $ext.TrimStart(".")

try {
    New-Item -ItemType Directory -Force -Path (Join-Path $workDir ".intelligencex") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $workDir "Analysis/Catalog/rules/internal") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $workDir "Analysis/Packs") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $workDir "src") | Out-Null

    Set-Content -LiteralPath (Join-Path $workDir ".intelligencex/reviewer.json") -Value @'
{
  "analysis": {
    "enabled": true,
    "packs": ["intelligencex-maintainability-default"]
  }
}
'@ -Encoding UTF8

    Set-Content -LiteralPath (Join-Path $workDir "Analysis/Catalog/rules/internal/IXDUP001.json") -Value @"
{
  "id": "IXDUP001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXDUP001",
  "title": "Duplication benchmark rule",
  "description": "Synthetic duplication benchmark rule.",
  "category": "Maintainability",
  "defaultSeverity": "warning",
  "tags": [
    "max-duplication-percent:25",
    "dup-window-lines:8",
    "include-ext:$extWithoutDot"
  ]
}
"@ -Encoding UTF8

    Set-Content -LiteralPath (Join-Path $workDir "Analysis/Packs/intelligencex-maintainability-default.json") -Value @'
{
  "id": "intelligencex-maintainability-default",
  "label": "IntelligenceX Maintainability",
  "rules": ["IXDUP001"]
}
'@ -Encoding UTF8

    Write-Host "Generating synthetic sources: files=$files lines=$lines language=$languageName workspace=$workDir"
    for ($i = 1; $i -le $files; $i++) {
        $filePath = Join-Path $workDir ("src/sample_" + $i + $ext)
        New-SourceFile -Path $filePath -LanguageName $languageName -Index $i -Lines $lines
    }

    $start = Get-Date
    Invoke-Checked @(
        "dotnet", "run", "--project", "IntelligenceX.Cli/IntelligenceX.Cli.csproj", "--framework", $framework, "--",
        "analyze", "run",
        "--workspace", $workDir,
        "--config", (Join-Path $workDir ".intelligencex/reviewer.json"),
        "--out", (Join-Path $workDir "artifacts")
    )
    $elapsed = ((Get-Date) - $start).TotalSeconds

    $metricsFile = Join-Path $workDir "artifacts/intelligencex.duplication.json"
    if (-not (Test-Path -LiteralPath $metricsFile -PathType Leaf)) {
        throw "ERROR: expected duplication metrics file was not produced: $metricsFile"
    }

    $metrics = Get-Content -LiteralPath $metricsFile -Raw | ConvertFrom-Json
    $totalSignificant = [int]($metrics.totalSignificantLines ?? 0)
    $duplicatedSignificant = [int]($metrics.duplicatedSignificantLines ?? 0)
    $overallPercent = [double]($metrics.overallDuplicatedPercent ?? 0)

    if ($elapsed -le 0) {
        $filesPerSecond = 0
        $linesPerSecond = 0
    } else {
        $filesPerSecond = $files / $elapsed
        $linesPerSecond = $totalSignificant / $elapsed
    }

    Write-Host "Duplication benchmark complete"
    Write-Host ("- Elapsed seconds: {0:N3}" -f $elapsed)
    Write-Host "- Files generated: $files"
    Write-Host "- Significant lines: $totalSignificant"
    Write-Host "- Duplicated significant lines: $duplicatedSignificant"
    Write-Host ("- Overall duplicated percent: {0}" -f $overallPercent)
    Write-Host ("- Throughput (files/sec): {0:N2}" -f $filesPerSecond)
    Write-Host ("- Throughput (significant-lines/sec): {0:N2}" -f $linesPerSecond)
} finally {
    if ($keepWorkDir) {
        Write-Host "Workspace preserved at: $workDir"
    } else {
        try {
            if (Test-Path -LiteralPath $workDir) {
                Remove-Item -LiteralPath $workDir -Recurse -Force
            }
        } catch {
            # Best-effort cleanup.
        }
    }
}
