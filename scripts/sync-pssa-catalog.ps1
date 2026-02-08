param(
    [Parameter()][string]$OutDir = (Join-Path -Path $PSScriptRoot -ChildPath (Join-Path -Path '..' -ChildPath (Join-Path -Path 'Analysis' -ChildPath (Join-Path -Path 'Catalog' -ChildPath (Join-Path -Path 'rules' -ChildPath 'powershell'))))),
    [Parameter()][switch]$PruneStale,
    [Parameter()][switch]$ForcePrune
)

$ErrorActionPreference = 'Stop'

$module = Get-Module -ListAvailable -Name PSScriptAnalyzer |
    Sort-Object Version -Descending |
    Select-Object -First 1
if (-not $module) {
    throw 'PSScriptAnalyzer module not found. Install with: Install-Module PSScriptAnalyzer -Scope CurrentUser'
}

# Avoid importing a module that is (accidentally or maliciously) located under the repo workspace.
$workspaceRoot = (Resolve-Path (Join-Path -Path $PSScriptRoot -ChildPath '..')).Path
if ($module.ModuleBase) {
    $root = [System.IO.Path]::GetFullPath($workspaceRoot)
    $base = [System.IO.Path]::GetFullPath($module.ModuleBase)
    $rootTrim = $root.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $rootPrefix = $rootTrim + [System.IO.Path]::DirectorySeparatorChar

    $isInWorkspace =
        $base.Equals($rootTrim, [System.StringComparison]::OrdinalIgnoreCase) -or
        $base.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)
    if ($isInWorkspace) {
        throw ("Refusing to import PSScriptAnalyzer from workspace path: {0}" -f $base)
    }
}

if ($module.Path) {
    Import-Module -Name $module.Path -RequiredVersion $module.Version -ErrorAction Stop
} else {
    Import-Module -Name PSScriptAnalyzer -RequiredVersion $module.Version -ErrorAction Stop
}

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$intendedOutDir = [System.IO.Path]::GetFullPath((Join-Path -Path $workspaceRoot -ChildPath (Join-Path -Path 'Analysis' -ChildPath (Join-Path -Path 'Catalog' -ChildPath (Join-Path -Path 'rules' -ChildPath 'powershell')))))
$resolvedOutDir = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $OutDir).Path)

# Even with -ForcePrune, never allow pruning outside the repo workspace.
$workspaceFull = [System.IO.Path]::GetFullPath($workspaceRoot)
$workspaceTrim = $workspaceFull.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$workspacePrefix = $workspaceTrim + [System.IO.Path]::DirectorySeparatorChar
$isUnderWorkspace =
    $resolvedOutDir.Equals($workspaceTrim, [System.StringComparison]::OrdinalIgnoreCase) -or
    $resolvedOutDir.StartsWith($workspacePrefix, [System.StringComparison]::OrdinalIgnoreCase)
if ($PruneStale -and (-not $isUnderWorkspace)) {
    throw ("Refusing to prune outside workspace. OutDir='{0}', workspace='{1}'." -f $resolvedOutDir, $workspaceTrim)
}

if ($PruneStale -and (-not $ForcePrune) -and (-not $resolvedOutDir.Equals($intendedOutDir, [System.StringComparison]::OrdinalIgnoreCase))) {
    throw ("Refusing to prune outside intended catalog directory. OutDir='{0}', intended='{1}'. Pass -ForcePrune to override." -f $resolvedOutDir, $intendedOutDir)
}

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

function Format-MetadataText([string]$text) {
    if ([string]::IsNullOrWhiteSpace($text)) { return '' }
    $fixed = $text
    # Upstream typos we don't want to publish as-is.
    $fixed = $fixed -replace '\bwhitepsace\b', 'whitespace'
    $fixed = $fixed -replace '\bautomtic\b', 'automatic'
    $fixed = $fixed -replace '\breadonly\b', 'read-only'
    $fixed = $fixed -replace '\bPrameter\b', 'Parameter'
    $fixed = $fixed -replace '\bequaltiy\b', 'equality'
    $fixed = $fixed -replace '\bcomaprision\b', 'comparison'
    $fixed = $fixed -replace '\bfunctiosn\b', 'functions'
    $fixed = $fixed -replace '\bindenation\b', 'indentation'
    $fixed = $fixed -replace '\bassigment\b', 'assignment'
    $fixed = $fixed -replace '\bIN\b', 'in'
    # Clean up accidental double periods that occasionally show up upstream (e.g. "ignored.. To").
    $fixed = $fixed -replace '\.\.\s+', '. '
    $fixed = $fixed -replace '\.\.$', '.'
    $fixed
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
$ruleIds = @($rules | ForEach-Object { [string]$_.RuleName } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$ruleIdSet = @{}
foreach ($id in $ruleIds) { $ruleIdSet[$id] = $true }

function Get-LearnDocsUrl([string]$ruleName) {
    if ([string]::IsNullOrWhiteSpace($ruleName)) { return $null }
    $name = $ruleName.Trim()
    if ($name.StartsWith('PS', [System.StringComparison]::OrdinalIgnoreCase)) {
        $name = $name.Substring(2)
    }
    $slug = $name.ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($slug)) { return $null }
    return ('https://learn.microsoft.com/powershell/utility-modules/psscriptanalyzer/rules/{0}' -f $slug)
}

foreach ($rule in $rules) {
    $ruleName = [string]$rule.RuleName
    if ([string]::IsNullOrWhiteSpace($ruleName)) { continue }

    $title = Compress-Whitespace ([string]$rule.CommonName)
    if ([string]::IsNullOrWhiteSpace($title)) { $title = Get-RuleTitleFromRuleName $ruleName }
    if ([string]::IsNullOrWhiteSpace($title)) { $title = $ruleName }

    $description = Compress-Whitespace ([string]$rule.Description)
    if ([string]::IsNullOrWhiteSpace($description)) { $description = "PSScriptAnalyzer rule '$ruleName'. See docs for details." }

    $title = Format-MetadataText $title
    $description = Format-MetadataText $description

    switch ($ruleName) {
        'PSAvoidAssignmentToAutomaticVariable' {
            $title = 'Changing automatic variables might have undesired side effects'
            $description = 'Automatic variables are built into PowerShell and are read-only. Avoid assigning to them.'
        }
        'PSAlignAssignmentStatement' {
            $title = 'Align Assignment Statements'
            $description = 'Line up assignment statements so that the assignment operators are aligned.'
        }
        'PSPossibleIncorrectComparisonWithNull' {
            $title = 'Possible Incorrect Comparison With Null'
            $description = 'Checks that $null is on the left side of any equality comparisons (eq, ne, ceq, cne, ieq, ine). When there is an array on the left side of a null equality comparison, PowerShell will check for a $null in the array rather than whether the array is null. If the two sides of the comparison are switched, this is fixed. Therefore, $null should always be on the left side of equality comparisons just in case.'
        }
        'PSUseConsistentWhitespace' {
            $title = 'Use Consistent Whitespace'
            $description = "Check for whitespace between keyword and open paren/curly, around assignment operator ('='), around arithmetic operators and after separators (',' and ';')."
        }
    }

    # Minor grammar cleanup
    $description = $description -replace '\boperator are\b', 'operators are'
    if (-not [string]::IsNullOrWhiteSpace($description) -and $description -notmatch '[\.\!\?]$') {
        $description = $description + '.'
    }

    $path = Join-Path $OutDir ($ruleName + '.json')
    $docs = $null
    if (Test-Path -LiteralPath $path) {
        try {
            $existing = Get-Content -LiteralPath $path -Raw -ErrorAction Stop | ConvertFrom-Json
            $docs = [string]$existing.docs
            if ([string]::IsNullOrWhiteSpace($docs)) { $docs = $null }
        } catch {
            $docs = $null
        }
    }
    if (-not $docs) {
        $docs = Get-LearnDocsUrl $ruleName
        if ([string]::IsNullOrWhiteSpace($docs)) { $docs = $null }
    }

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
    }
    if ($docs) { $obj.docs = $docs }

    $json = $obj | ConvertTo-Json -Depth 6
    Write-FileUtf8NoBomLf $path $json
}

# Delete stale rule files so the repo doesn't accumulate orphaned rules over time.
$existingRuleFiles = @(Get-ChildItem -LiteralPath $OutDir -Filter '*.json' -File -ErrorAction SilentlyContinue)
$staleRuleFiles = @()
foreach ($file in $existingRuleFiles) {
    $id = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    if ($id -and -not $ruleIdSet.ContainsKey($id)) {
        $staleRuleFiles += $file
    }
}

# Also delete stale overrides for rules that no longer exist.
$staleOverrideFiles = @()
if ($resolvedOutDir.Equals($intendedOutDir, [System.StringComparison]::OrdinalIgnoreCase)) {
    $rulesRoot = Split-Path -Parent $OutDir
    $catalogRoot = Split-Path -Parent $rulesRoot
    $overridesDir = Join-Path -Path $catalogRoot -ChildPath (Join-Path -Path 'overrides' -ChildPath 'powershell')
    if (Test-Path -LiteralPath $overridesDir) {
        foreach ($file in @(Get-ChildItem -LiteralPath $overridesDir -Filter '*.json' -File -ErrorAction SilentlyContinue)) {
            $id = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
            if ($id -and -not $ruleIdSet.ContainsKey($id)) { $staleOverrideFiles += $file }
        }
    }
} elseif ($PruneStale) {
    Write-Warning ("Skipping overrides pruning because OutDir does not match intended catalog directory. OutDir='{0}', intended='{1}'." -f $resolvedOutDir, $intendedOutDir)
}

$deleted = 0
if (($staleRuleFiles.Count -gt 0) -or ($staleOverrideFiles.Count -gt 0)) {
    $totalStale = $staleRuleFiles.Count + $staleOverrideFiles.Count
    if (-not $PruneStale) {
        Write-Warning ("Found {0} stale file(s). Re-run with -PruneStale to delete them." -f $totalStale)
    } else {
        foreach ($file in $staleRuleFiles) {
            Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop
            $deleted++
        }
        foreach ($file in $staleOverrideFiles) {
            Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop
            $deleted++
        }
    }
}

Write-Output ("Wrote {0} rule file(s) to {1} (deleted {2} stale file(s))" -f $rules.Count, $OutDir, $deleted)
