param(
    [Parameter()][string]$OutDir = (Join-Path -Path $PSScriptRoot -ChildPath (Join-Path -Path '..' -ChildPath (Join-Path -Path 'Analysis' -ChildPath (Join-Path -Path 'Catalog' -ChildPath (Join-Path -Path 'rules' -ChildPath 'powershell')))))
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Module -ListAvailable -Name PSScriptAnalyzer)) {
    throw 'PSScriptAnalyzer module not found. Install with: Install-Module PSScriptAnalyzer -Scope CurrentUser'
}

Import-Module PSScriptAnalyzer -ErrorAction Stop

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$docsBase = 'https://learn.microsoft.com/powershell/utility-modules/psscriptanalyzer/rules/'
$securityRules = @(
    'PSAvoidUsingAllowUnencryptedAuthentication',
    'PSAvoidUsingBrokenHashAlgorithms',
    'PSAvoidUsingConvertToSecureStringWithPlainText',
    'PSAvoidUsingInvokeExpression',
    'PSAvoidUsingPlainTextForPassword',
    'PSAvoidUsingUsernameAndPasswordParams',
    'PSUsePSCredentialType'
) | ForEach-Object { $_.ToLowerInvariant() }

function Get-Category([string]$ruleName) {
    if ($securityRules -contains $ruleName.ToLowerInvariant()) { return 'Security' }
    if ($ruleName.StartsWith('PSPossibleIncorrect', [System.StringComparison]::OrdinalIgnoreCase)) { return 'Reliability' }
    return 'BestPractices'
}

function Get-DefaultSeverity([string]$severity) {
    switch ($severity) {
        'Error' { return 'error' }
        'Information' { return 'info' }
        default { return 'warning' }
    }
}

function Compress-Whitespace([string]$text) {
    if ([string]::IsNullOrWhiteSpace($text)) { return '' }
    ($text -replace '[\r\n]+', ' ' -replace '\s+', ' ').Trim()
}

function Get-KnownTypoFixedText([string]$text) {
    if ([string]::IsNullOrWhiteSpace($text)) { return '' }
    $t = $text

    # Common upstream misspellings observed in PSScriptAnalyzer rule metadata.
    $t = $t -ireplace '\bautomtic\b', 'automatic'
    $t = $t -ireplace '\bfunctiosn\b', 'functions'
    $t = $t -ireplace '\bprameter\b', 'parameter'
    $t = $t -ireplace '\bequaltiy\b', 'equality'
    $t = $t -ireplace '\bcomaprision\b', 'comparison'
    $t = $t -ireplace '\bcomaprision(s)?\b', 'comparison$1'
    $t = $t -ireplace '\bassigment\b', 'assignment'

    $t
}

function Get-RuleTitleFromRuleName([string]$ruleName) {
    if ([string]::IsNullOrWhiteSpace($ruleName)) { return '' }
    $name = $ruleName.Trim()
    if ($name.StartsWith('PS', [System.StringComparison]::OrdinalIgnoreCase)) {
        $name = $name.Substring(2)
    }
    # Insert spaces for PascalCase while keeping acronyms (UTF8, DSC, WMI) reasonably intact.
    $name = $name -creplace '([a-z0-9])([A-Z])', '$1 $2'
    $name = $name -creplace '([A-Z])([A-Z][a-z])', '$1 $2'
    $name.Trim()
}

function Write-FileUtf8NoBomLf([string]$path, [string]$content) {
    # Avoid BOM differences across PowerShell versions and normalize newlines for stable diffs.
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $normalized = ($content -replace "`r`n", "`n" -replace "`r", "`n")
    if (-not $normalized.EndsWith("`n")) { $normalized += "`n" }
    [System.IO.File]::WriteAllText($path, $normalized, $utf8NoBom)
}

$rules = Get-ScriptAnalyzerRule | Sort-Object RuleName

foreach ($rule in $rules) {
    $ruleName = [string]$rule.RuleName
    if ([string]::IsNullOrWhiteSpace($ruleName)) { continue }

    $slug = $ruleName
    if ($slug.StartsWith('PS', [System.StringComparison]::OrdinalIgnoreCase)) {
        $slug = $slug.Substring(2)
    }
    $slug = $slug.ToLowerInvariant()

    $title = Get-KnownTypoFixedText (Compress-Whitespace ([string]$rule.CommonName))
    if ([string]::IsNullOrWhiteSpace($title)) { $title = Get-RuleTitleFromRuleName $ruleName }
    if ([string]::IsNullOrWhiteSpace($title)) { $title = $ruleName }

    $description = Get-KnownTypoFixedText (Compress-Whitespace ([string]$rule.Description))
    if ([string]::IsNullOrWhiteSpace($description)) { $description = "PSScriptAnalyzer rule '$ruleName'. See docs for details." }

    $category = Get-Category $ruleName
    $defaultSeverity = Get-DefaultSeverity ([string]$rule.Severity)

    $tags = @('powershell', 'psscriptanalyzer')
    if ($category -eq 'Security') { $tags += 'security' }
    if ($category -eq 'Reliability') { $tags += 'reliability' }
    if ($category -eq 'BestPractices') { $tags += 'best-practices' }

    $obj = [ordered]@{
        id = $ruleName
        language = 'powershell'
        tool = 'PSScriptAnalyzer'
        toolRuleId = $ruleName
        title = $title
        description = $description
        category = $category
        defaultSeverity = $defaultSeverity
        tags = $tags
        docs = ($docsBase + $slug)
    }

    $json = $obj | ConvertTo-Json -Depth 6
    $path = Join-Path $OutDir ($ruleName + '.json')
    Write-FileUtf8NoBomLf $path $json
}

Write-Output ("Wrote {0} rule file(s) to {1}" -f $rules.Count, $OutDir)
