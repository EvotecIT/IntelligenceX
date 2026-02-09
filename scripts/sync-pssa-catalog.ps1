param(
    [Parameter()][string]$OutDir = (Join-Path -Path $PSScriptRoot -ChildPath (Join-Path -Path '..' -ChildPath (Join-Path -Path 'Analysis' -ChildPath (Join-Path -Path 'Catalog' -ChildPath (Join-Path -Path 'rules' -ChildPath 'powershell'))))),
    [Parameter()][switch]$PruneStale,
    [Parameter()][switch]$ForcePrune,
    [Parameter()][switch]$AllowNonIntendedOutDir,
    [Parameter()][switch]$AllowUntrustedModuleBase
)

$ErrorActionPreference = 'Stop'

$runningOnWindows = ($env:OS -eq 'Windows_NT')
$pathComparison = if ($runningOnWindows) { [System.StringComparison]::OrdinalIgnoreCase } else { [System.StringComparison]::Ordinal }

function Get-NormalizedPath([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return '' }

    # Expand ~ and resolve relative paths without requiring the target to exist.
    $unresolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($path)
    try {
        $resolved = (Resolve-Path -LiteralPath $unresolved -ErrorAction Stop).Path
        return [System.IO.Path]::GetFullPath($resolved)
    } catch {
        return [System.IO.Path]::GetFullPath($unresolved)
    }
}

function ConvertTo-JsonEscapedString([string]$value) {
    if ($null -eq $value) { return '' }
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $value.ToCharArray()) {
        $code = [int][char]$ch
        switch ($ch) {
            '"' { [void]$sb.Append('\"'); continue }
            '\' { [void]$sb.Append('\\'); continue }
            "`b" { [void]$sb.Append('\b'); continue }
            "`f" { [void]$sb.Append('\f'); continue }
            "`n" { [void]$sb.Append('\n'); continue }
            "`r" { [void]$sb.Append('\r'); continue }
            "`t" { [void]$sb.Append('\t'); continue }
        }
        if ($code -lt 0x20) {
            [void]$sb.Append(("\\u{0:x4}" -f $code))
            continue
        }
        [void]$sb.Append($ch)
    }
    return $sb.ToString()
}

function ConvertTo-DeterministicJson([System.Collections.IDictionary]$obj) {
    if ($null -eq $obj) { throw 'JSON object cannot be null' }

    # Keep a stable, intentional order for our known schema keys, and sort any extras for determinism.
    $preferredOrder = @(
        'id',
        'language',
        'tool',
        'toolRuleId',
        'title',
        'description',
        'category',
        'defaultSeverity',
        'tags',
        'docs'
    )
    $ordered = @()
    foreach ($k in $preferredOrder) {
        if ($obj.Contains($k)) { $ordered += $k }
    }
    $extras = @($obj.Keys | Where-Object { $preferredOrder -notcontains $_ } | Sort-Object)
    $keys = @($ordered + $extras)
    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add('{')
    for ($i = 0; $i -lt $keys.Count; $i++) {
        $key = [string]$keys[$i]
        $value = $obj[$key]
        $comma = if ($i -lt ($keys.Count - 1)) { ',' } else { '' }

        if ($value -is [System.Array]) {
            [void]$lines.Add(('  "{0}": [' -f (ConvertTo-JsonEscapedString $key)))
            $arr = @($value)
            for ($j = 0; $j -lt $arr.Count; $j++) {
                $itemComma = if ($j -lt ($arr.Count - 1)) { ',' } else { '' }
                $item = [string]$arr[$j]
                [void]$lines.Add(('    "{0}"{1}' -f (ConvertTo-JsonEscapedString $item), $itemComma))
            }
            [void]$lines.Add(('  ]{0}' -f $comma))
            continue
        }

        if ($null -eq $value) {
            [void]$lines.Add(('  "{0}": null{1}' -f (ConvertTo-JsonEscapedString $key), $comma))
            continue
        }

        if ($value -isnot [string]) {
            throw ("Unsupported JSON value type for key '{0}': {1}" -f $key, $value.GetType().FullName)
        }

        [void]$lines.Add(('  "{0}": "{1}"{2}' -f (ConvertTo-JsonEscapedString $key), (ConvertTo-JsonEscapedString $value), $comma))
    }
    [void]$lines.Add('}')
    return ($lines -join "`n")
}

$module = Get-Module -ListAvailable -Name PSScriptAnalyzer |
    Sort-Object Version -Descending |
    Select-Object -First 1
if (-not $module) {
    throw 'PSScriptAnalyzer module not found. Install with: Install-Module PSScriptAnalyzer -Scope CurrentUser'
}

# Avoid importing a module that is (accidentally or maliciously) located under the repo workspace.
$workspaceRoot = (Resolve-Path (Join-Path -Path $PSScriptRoot -ChildPath '..')).Path
if ($module.ModuleBase) {
    $root = Get-NormalizedPath $workspaceRoot
    $base = Get-NormalizedPath $module.ModuleBase
    $rootTrim = $root.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $rootPrefix = $rootTrim + [System.IO.Path]::DirectorySeparatorChar

    $isInWorkspace =
        $base.Equals($rootTrim, $pathComparison) -or
        $base.StartsWith($rootPrefix, $pathComparison)
    if ($isInWorkspace) {
        throw ("Refusing to import PSScriptAnalyzer from workspace path: {0}" -f $base)
    }
}

function Test-IsUnderPath([string]$path, [string]$root) {
    if ([string]::IsNullOrWhiteSpace($path) -or [string]::IsNullOrWhiteSpace($root)) { return $false }
    try {
        $fullPath = Get-NormalizedPath $path
        $fullRoot = Get-NormalizedPath $root
    } catch {
        return $false
    }
    $trimRoot = $fullRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $prefix = $trimRoot + [System.IO.Path]::DirectorySeparatorChar
    return $fullPath.Equals($trimRoot, $pathComparison) -or
        $fullPath.StartsWith($prefix, $pathComparison)
}

function Test-IsTrustedModuleBase([string]$moduleBase) {
    if ([string]::IsNullOrWhiteSpace($moduleBase)) { return $false }
    $trustedRoots = @()

    # PSHOME modules are considered trusted.
    if ($PSHOME) { $trustedRoots += (Join-Path -Path $PSHOME -ChildPath 'Modules') }

    # Common system-wide module locations.
    if ($runningOnWindows) {
        if ($env:ProgramFiles) {
            $trustedRoots += (Join-Path -Path $env:ProgramFiles -ChildPath (Join-Path -Path 'WindowsPowerShell' -ChildPath 'Modules'))
            $trustedRoots += (Join-Path -Path $env:ProgramFiles -ChildPath (Join-Path -Path 'PowerShell' -ChildPath 'Modules'))
        }
    } else {
        $trustedRoots += '/usr/local/share/powershell/Modules'
        $trustedRoots += '/usr/share/powershell/Modules'
    }

    foreach ($root in $trustedRoots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        if (Test-IsUnderPath $moduleBase $root) { return $true }
    }
    return $false
}

function Test-IsTrustedAuthenticode([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return $false }
    if (-not $runningOnWindows) { return $false }
    try {
        $sig = Get-AuthenticodeSignature -FilePath $path
        if ($sig -and $sig.Status -eq 'Valid' -and $sig.SignerCertificate -and $sig.SignerCertificate.Subject -like '*Microsoft Corporation*') {
            return $true
        }
    } catch {
        # Ignore Authenticode lookup failures (not all platforms support it consistently).
        Write-Verbose ("Authenticode signature check failed for '{0}': {1}" -f $path, $_.Exception.Message)
    }
    return $false
}

# Refuse to import PSScriptAnalyzer from arbitrary PSModulePath locations. Prefer PSHOME/system paths.
$trustedBase = $false
if ($module.ModuleBase) {
    $trustedBase = Test-IsTrustedModuleBase $module.ModuleBase
}
$trustedSig = $false
if ($runningOnWindows) {
    $verifyPaths = @()
    if ($module.Path) { $verifyPaths += $module.Path }
    if ($module.ModuleBase -and $module.RootModule) {
        $rootCandidate = Join-Path -Path $module.ModuleBase -ChildPath $module.RootModule
        if (Test-Path -LiteralPath $rootCandidate) { $verifyPaths += $rootCandidate }
    }
    foreach ($verifyPath in $verifyPaths) {
        if (Test-IsTrustedAuthenticode $verifyPath) {
            $trustedSig = $true
            break
        }
    }
}
if (-not ($trustedBase -or $trustedSig)) {
    if ($AllowUntrustedModuleBase) {
        Write-Warning ("Importing PSScriptAnalyzer from an untrusted location because -AllowUntrustedModuleBase was set. ModuleBase='{0}', Path='{1}'." -f $module.ModuleBase, $module.Path)
    } else {
        $baseMsg = $module.ModuleBase
        $pathMsg = $module.Path
        throw ("Refusing to import PSScriptAnalyzer from an untrusted location. ModuleBase='{0}', Path='{1}'. Install with -Scope AllUsers, or pass -AllowUntrustedModuleBase to proceed anyway." -f $baseMsg, $pathMsg)
    }
}

# Import by module name + pinned version, then verify the imported module matches the one we inspected above.
# This avoids path-based imports while still defending against module shadowing.
Import-Module -Name PSScriptAnalyzer -RequiredVersion $module.Version -ErrorAction Stop
$imported = Get-Module -Name PSScriptAnalyzer -ErrorAction Stop |
    Where-Object { $_.Version -eq $module.Version } |
    Select-Object -First 1
if (-not $imported) {
    throw ("Failed to import PSScriptAnalyzer {0}." -f $module.Version)
}
if ($module.ModuleBase -and $imported.ModuleBase) {
    $expectedBase = Get-NormalizedPath $module.ModuleBase
    $actualBase = Get-NormalizedPath $imported.ModuleBase
    if (-not $actualBase.Equals($expectedBase, $pathComparison)) {
        throw ("Imported PSScriptAnalyzer ModuleBase does not match expected ModuleBase. Expected='{0}', actual='{1}'." -f $expectedBase, $actualBase)
    }
}
if ($module.Path -and $imported.Path) {
    $expectedPath = Get-NormalizedPath $module.Path
    $actualPath = Get-NormalizedPath $imported.Path
    if (-not $actualPath.Equals($expectedPath, $pathComparison)) {
        throw ("Imported PSScriptAnalyzer Path does not match expected Path. Expected='{0}', actual='{1}'." -f $expectedPath, $actualPath)
    }
}

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$intendedOutDir = [System.IO.Path]::GetFullPath((Join-Path -Path $workspaceRoot -ChildPath (Join-Path -Path 'Analysis' -ChildPath (Join-Path -Path 'Catalog' -ChildPath (Join-Path -Path 'rules' -ChildPath 'powershell')))))
try {
    # Resolve from the existing directory (created above) to keep pruning decisions predictable.
    $resolvedOutDir = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $OutDir -ErrorAction Stop).Path)
} catch {
    throw ("Failed to resolve OutDir '{0}': {1}" -f $OutDir, $_.Exception.Message)
}
$resolvedIntendedOutDir = $intendedOutDir
if (Test-Path -LiteralPath $intendedOutDir) {
    try {
        $resolvedIntendedOutDir = Get-NormalizedPath $intendedOutDir
    } catch {
        throw ("Failed to resolve intended OutDir '{0}': {1}" -f $intendedOutDir, $_.Exception.Message)
    }
}

# Even with -ForcePrune, never allow pruning outside the repo workspace.
try {
    $workspaceFull = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $workspaceRoot -ErrorAction Stop).Path)
} catch {
    throw ("Failed to resolve workspace root '{0}': {1}" -f $workspaceRoot, $_.Exception.Message)
}
$workspaceTrim = $workspaceFull.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$workspacePrefix = $workspaceTrim + [System.IO.Path]::DirectorySeparatorChar
$isUnderWorkspace =
    $resolvedOutDir.Equals($workspaceTrim, $pathComparison) -or
    $resolvedOutDir.StartsWith($workspacePrefix, $pathComparison)
if ($PruneStale -and (-not $isUnderWorkspace)) {
    throw ("Refusing to prune outside workspace. OutDir='{0}', workspace='{1}'." -f $resolvedOutDir, $workspaceTrim)
}

if ($PruneStale -and (-not $resolvedOutDir.Equals($resolvedIntendedOutDir, $pathComparison))) {
    if (-not $ForcePrune) {
        throw ("Refusing to prune outside intended catalog directory. OutDir='{0}', intended='{1}'. Pass -ForcePrune to prune." -f $resolvedOutDir, $resolvedIntendedOutDir)
    }
    if (-not $AllowNonIntendedOutDir) {
        throw ("Refusing to prune outside intended catalog directory. OutDir='{0}', intended='{1}'. Pass -AllowNonIntendedOutDir to explicitly allow pruning a non-standard directory." -f $resolvedOutDir, $resolvedIntendedOutDir)
    }
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
$ruleIdSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($id in $ruleIds) { [void]$ruleIdSet.Add($id) }
if ($ruleIdSet.Count -ne $ruleIds.Count) {
    $counts = @{}
    foreach ($id in $ruleIds) {
        $key = $id.ToLowerInvariant()
        if ($counts.ContainsKey($key)) { $counts[$key]++ } else { $counts[$key] = 1 }
    }
    $duplicates = @($counts.GetEnumerator() | Where-Object { $_.Value -gt 1 } | ForEach-Object { $_.Key } | Sort-Object)
    throw ("Duplicate PSScriptAnalyzer rule names detected ({0}): {1}" -f $duplicates.Count, ($duplicates -join ', '))
}

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

$existingDocsByRule = @{}
try {
    if (Test-Path -LiteralPath $OutDir) {
        foreach ($file in [System.IO.Directory]::EnumerateFiles($OutDir, '*.json')) {
            $id = [System.IO.Path]::GetFileNameWithoutExtension($file)
            if ([string]::IsNullOrWhiteSpace($id)) { continue }
            try {
                $existing = Get-Content -LiteralPath $file -Raw -ErrorAction Stop | ConvertFrom-Json
                $existingDocs = [string]$existing.docs
                if (-not [string]::IsNullOrWhiteSpace($existingDocs)) {
                    $existingDocsByRule[$id] = $existingDocs.Trim()
                }
            } catch {
                # Ignore read/parse errors; we can still generate stable docs from Learn.
                continue
            }
        }
    }
} catch {
    throw ("Failed to preload existing docs from OutDir '{0}': {1}" -f $OutDir, $_.Exception.Message)
}

foreach ($rule in $rules) {
    $ruleName = [string]$rule.RuleName
    if ([string]::IsNullOrWhiteSpace($ruleName)) { continue }
    if ($ruleName.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) {
        throw ("RuleName contains invalid filename characters: '{0}'." -f $ruleName)
    }

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

    # Prefer a stable Learn URL. Only preserve existing docs if it matches the expected Learn URL exactly
    # (no query/fragment and correct slug), to avoid carrying forward bad/unstable URLs forever.
    $docs = Get-LearnDocsUrl $ruleName
    if ([string]::IsNullOrWhiteSpace($docs)) {
        throw ("Failed to compute Learn docs URL for rule '{0}'." -f $ruleName)
    }
    if ($existingDocsByRule.ContainsKey($ruleName)) {
        $existingDocs = [string]$existingDocsByRule[$ruleName]
        if (-not [string]::IsNullOrWhiteSpace($existingDocs)) {
            try {
                $existingUri = [System.Uri]::new($existingDocs)
                $expectedUri = [System.Uri]::new($docs)
                if ($existingUri.Scheme -eq 'https' -and
                    $existingUri.Host -eq 'learn.microsoft.com' -and
                    [string]::IsNullOrEmpty($existingUri.Query) -and
                    [string]::IsNullOrEmpty($existingUri.Fragment) -and
                    $existingUri.AbsolutePath -eq $expectedUri.AbsolutePath) {
                    $docs = $existingDocs
                }
            } catch {
                # Ignore invalid existing docs; fall back to Learn.
                $existingDocs = $null
            }
        }
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
        docs = $docs
    }

    $json = ConvertTo-DeterministicJson $obj
    Write-FileUtf8NoBomLf $path $json
}

# Delete stale rule files so the repo doesn't accumulate orphaned rules over time.
$existingRuleFiles = @()
try {
    $existingRuleFiles = @(Get-ChildItem -LiteralPath $OutDir -Filter '*.json' -File -ErrorAction Stop)
} catch {
    throw ("Failed to enumerate existing rule JSON files in OutDir '{0}': {1}" -f $OutDir, $_.Exception.Message)
}
$staleRuleFiles = @()
foreach ($file in $existingRuleFiles) {
    $id = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    if ($id -and -not $ruleIdSet.Contains($id)) {
        $staleRuleFiles += $file
    }
}

# Also delete stale overrides for rules that no longer exist.
$staleOverrideFiles = @()
if ($resolvedOutDir.Equals($resolvedIntendedOutDir, $pathComparison)) {
    $rulesRoot = Split-Path -Parent $OutDir
    $catalogRoot = Split-Path -Parent $rulesRoot
    $overridesDir = Join-Path -Path $catalogRoot -ChildPath (Join-Path -Path 'overrides' -ChildPath 'powershell')
    if (Test-Path -LiteralPath $overridesDir) {
        try {
            foreach ($file in @(Get-ChildItem -LiteralPath $overridesDir -Filter '*.json' -File -ErrorAction Stop)) {
                $id = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
                if ($id -and -not $ruleIdSet.Contains($id)) { $staleOverrideFiles += $file }
            }
        } catch {
            throw ("Failed to enumerate override JSON files in '{0}': {1}" -f $overridesDir, $_.Exception.Message)
        }
    }
} elseif ($PruneStale) {
    Write-Warning ("Skipping overrides pruning because OutDir does not match intended catalog directory. OutDir='{0}', intended='{1}'." -f $resolvedOutDir, $resolvedIntendedOutDir)
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
