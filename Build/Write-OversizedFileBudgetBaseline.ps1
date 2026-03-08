param(
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [int] $MinimumLines = 700
)

$generatedPath = Join-Path $RepoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.App.Tests\OversizedFileBudgetBaseline.generated.txt'

$files = git -C $RepoRoot ls-files -- '*.cs' '*.js' '*.css' |
    Where-Object { $_ -notmatch '(^|/)(bin|obj|node_modules|Artifacts|_wt|vendor)(/|$)' }

$rows = foreach ($relativePath in $files) {
    $fullPath = Join-Path $RepoRoot $relativePath
    $lineCount = (Get-Content $fullPath | Measure-Object -Line).Lines
    if ($lineCount -gt $MinimumLines) {
        '{0}|{1}' -f ($relativePath -replace '\\', '/'), $lineCount
    }
}

$content = @(
    '# Generated oversized source file snapshot.'
    '# Refresh with:'
    '#   pwsh.exe -NoLogo -NoProfile -File Build/Write-OversizedFileBudgetBaseline.ps1'
    ''
) + ($rows | Sort-Object)

Set-Content -Path $generatedPath -Value $content
Write-Host "Updated $generatedPath"
