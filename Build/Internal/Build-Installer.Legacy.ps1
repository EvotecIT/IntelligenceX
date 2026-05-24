# Build IntelligenceX Chat MSI installer from a portable payload bundle.

[CmdletBinding()] param(
    [ValidateSet('win-x64','win-arm64')]
    [string] $Runtime = 'win-x64',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('host','app')]
    [string] $Frontend = 'app',

    [string] $Framework = 'net10.0-windows',
    [string] $AppFramework = 'net10.0-windows10.0.26100.0',
    [string] $PayloadDir,
    [string] $PortableOutDir,
    [string] $BundleName,
    [switch] $IncludeService,
    [switch] $IncludeSymbols,
    [string] $TestimoXRoot,

    [string] $ProductName = 'IntelligenceX Chat',
    [string] $Manufacturer = 'Evotec',
    [string] $ProductVersion,
    [string] $UpgradeCode = '{a2b787a5-f539-4763-add6-2baa2c2518c7}',

    [string] $OutDir,
    [switch] $Sign,
    [string] $SignToolPath = 'signtool.exe',
    [string] $SignThumbprint,
    [string] $SignSubjectName,
    [string] $SignTimestampUrl = 'http://timestamp.digicert.com',
    [string] $SignDescription = 'IntelligenceX Chat',
    [string] $SignUrl,
    [string] $SignCsp,
    [string] $SignKeyContainer,
    [bool] $UseTestimoXSignThumbprintFallback = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Step($text) { Write-Host "[+] $text" -ForegroundColor Yellow }
function Write-Ok($text) { Write-Host "[OK] $text" -ForegroundColor Green }
function Write-Warn($text) { Write-Host "[!] $text" -ForegroundColor DarkYellow }

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]] $Args,
        [string] $WorkingDirectory
    )

    if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $WorkingDirectory = $script:RepoRoot
    }

    Push-Location $WorkingDirectory
    try {
        & dotnet @Args
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet command failed with exit code ${LASTEXITCODE}: dotnet $($Args -join ' ')"
        }
    } finally {
        Pop-Location
    }
}

function Resolve-SignToolPath {
    param([string] $Path)
    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        $cmd = Get-Command $Path -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path $kitsRoot) {
        $versions = Get-ChildItem -Path $kitsRoot -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending
        foreach ($ver in $versions) {
            foreach ($arch in @('x64', 'x86')) {
                $candidate = Join-Path $ver.FullName (Join-Path $arch 'signtool.exe')
                if (Test-Path $candidate) { return $candidate }
            }
        }
    }
    return $null
}

function Get-ShortHash {
    param([string] $Text)
    $sha1 = [System.Security.Cryptography.SHA1]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text.ToLowerInvariant())
        $hash = $sha1.ComputeHash($bytes)
        return ($hash | ForEach-Object { $_.ToString('x2') }) -join ''
    } finally {
        $sha1.Dispose()
    }
}

function Write-HarvestWxs {
    param(
        [Parameter(Mandatory)]
        [string] $PayloadRoot,
        [Parameter(Mandatory)]
        [string] $OutputPath,
        [string[]] $ExcludeFiles
    )

    $excludeResolved = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    if ($ExcludeFiles -and $ExcludeFiles.Count -gt 0) {
        foreach ($excludeFile in $ExcludeFiles) {
            if ([string]::IsNullOrWhiteSpace($excludeFile)) {
                continue
            }

            $resolved = [System.IO.Path]::GetFullPath($excludeFile)
            [void] $excludeResolved.Add($resolved)
        }
    }

    $files = Get-ChildItem -Path $PayloadRoot -File -Recurse -ErrorAction Stop |
        Where-Object { -not $excludeResolved.Contains([System.IO.Path]::GetFullPath($_.FullName)) }

    $root = [ordered]@{
        Id = 'INSTALLFOLDER'
        Name = ''
        Children = @{}
        Files = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    }

    $dirIds = @{}
    function Get-DirId([string] $relPath) {
        if (-not $dirIds.ContainsKey($relPath)) {
            $hash = Get-ShortHash -Text $relPath
            $dirIds[$relPath] = "DIR_$($hash.Substring(0, 12))"
        }
        return $dirIds[$relPath]
    }

    foreach ($file in $files) {
        $relative = $file.FullName.Substring($PayloadRoot.Length).TrimStart('\', '/')
        $dir = [System.IO.Path]::GetDirectoryName($relative)
        $node = $root
        if ($dir) {
            $segments = $dir -split '[\\/]' | Where-Object { $_ -ne '' }
            $pathSoFar = ''
            foreach ($segment in $segments) {
                $pathSoFar = if ($pathSoFar) { Join-Path $pathSoFar $segment } else { $segment }
                if (-not $node.Children.ContainsKey($segment)) {
                    $node.Children[$segment] = [ordered]@{
                        Id = (Get-DirId -relPath $pathSoFar)
                        Name = $segment
                        Children = @{}
                        Files = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
                    }
                }
                $node = $node.Children[$segment]
            }
        }
        $node.Files.Add($file)
    }

    $componentIds = [System.Collections.Generic.List[string]]::new()
    $sb = [System.Text.StringBuilder]::new()

    function Esc([string] $value) {
        return [System.Security.SecurityElement]::Escape($value)
    }

    function Write-Components($filesForDir, [int] $indent) {
        foreach ($f in $filesForDir) {
            $rel = $f.FullName.Substring($PayloadRoot.Length).TrimStart('\', '/')
            $hash = Get-ShortHash -Text $rel
            $compId = "cmp_$($hash.Substring(0, 12))"
            $fileId = "fil_$($hash.Substring(0, 12))"
            $componentIds.Add($compId)
            $src = Esc($f.FullName)
            [void] $sb.Append(' ' * $indent).Append('<Component Id="').Append($compId).AppendLine('" Guid="*">')
            [void] $sb.Append(' ' * ($indent + 2)).Append('<File Id="').Append($fileId).Append('" Source="').Append($src).AppendLine('" KeyPath="yes" />')
            [void] $sb.Append(' ' * $indent).AppendLine('</Component>')
        }
    }

    function Write-DirectoryNode($node, [int] $indent) {
        foreach ($childKey in ($node.Children.Keys | Sort-Object)) {
            $child = $node.Children[$childKey]
            $childName = Esc $child.Name
            [void] $sb.Append(' ' * $indent).Append('<Directory Id="').Append($child.Id).Append('" Name="').Append($childName).AppendLine('">')
            Write-Components -filesForDir $child.Files -indent ($indent + 2)
            Write-DirectoryNode -node $child -indent ($indent + 2)
            [void] $sb.Append(' ' * $indent).AppendLine('</Directory>')
        }
    }

    [void] $sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
    [void] $sb.AppendLine('  <Fragment>')
    [void] $sb.AppendLine('    <DirectoryRef Id="INSTALLFOLDER">')
    Write-Components -filesForDir $root.Files -indent 6
    Write-DirectoryNode -node $root -indent 6
    [void] $sb.AppendLine('    </DirectoryRef>')
    [void] $sb.AppendLine('    <ComponentGroup Id="ProductFiles">')
    foreach ($id in $componentIds) {
        [void] $sb.Append('      ').Append('<ComponentRef Id="').Append($id).AppendLine('" />')
    }
    [void] $sb.AppendLine('    </ComponentGroup>')
    [void] $sb.AppendLine('  </Fragment>')
    [void] $sb.AppendLine('</Wix>')

    Set-Content -Path $OutputPath -Value $sb.ToString() -Encoding UTF8
}

function Test-HarvestPayloadManifest {
    param([Parameter(Mandatory)][string] $HarvestPath)

    if (-not (Test-Path $HarvestPath)) {
        throw "Harvest file was not generated: $HarvestPath"
    }

    [xml] $harvestXml = Get-Content $HarvestPath
    $ns = [System.Xml.XmlNamespaceManager]::new($harvestXml.NameTable)
    $ns.AddNamespace('w', 'http://wixtoolset.org/schemas/v4/wxs')

    $group = $harvestXml.SelectSingleNode('/w:Wix/w:Fragment/w:ComponentGroup[@Id="ProductFiles"]', $ns)
    if ($null -eq $group) {
        throw "Harvest file does not define ComponentGroup Id='ProductFiles': $HarvestPath"
    }

    $refs = $group.SelectNodes('w:ComponentRef', $ns)
    if ($null -eq $refs -or $refs.Count -eq 0) {
        throw "Harvest file defines ProductFiles but contains no ComponentRef entries: $HarvestPath"
    }
}

function Convert-ToMsiVersion {
    param([string] $RawVersion)

    if (-not [string]::IsNullOrWhiteSpace($RawVersion)) {
        try {
            $version = [Version](($RawVersion -split '[^0-9\.]')[0])
            $major = [Math]::Max(0, [Math]::Min($version.Major, 255))
            $minor = [Math]::Max(0, [Math]::Min($version.Minor, 255))
            $build = if ($version.Build -lt 0) { 0 } else { $version.Build }
            $build = [Math]::Max(0, [Math]::Min($build, 65535))
            return "{0}.{1}.{2}" -f $major, $minor, $build
        } catch {
        }
    }

    $now = Get-Date -AsUTC
    $major = [Math]::Max(0, [Math]::Min(($now.Year - 2000), 255))
    $minor = [Math]::Max(0, [Math]::Min($now.Month, 255))
    $build = [Math]::Max(0, [Math]::Min(($now.Day * 1000) + ($now.Hour * 10) + $now.Minute, 65535))
    return "{0}.{1}.{2}" -f $major, $minor, $build
}

function New-ShortPayloadJunction {
    param([Parameter(Mandatory)][string] $TargetPath)

    $junctionRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'ix-chat-installer'
    New-Item -ItemType Directory -Path $junctionRoot -Force | Out-Null

    $junctionPath = Join-Path $junctionRoot ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Junction -Path $junctionPath -Target $TargetPath | Out-Null
    return $junctionPath
}

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $script:RepoRoot 'Build\Internal\Resolve-ReleaseDefaults.ps1')
$packagePortableScript = Join-Path $script:RepoRoot 'Build\Advanced\Package-Portable.ps1'
$installerProject = Join-Path $script:RepoRoot 'Installer\IntelligenceX.Chat\IntelligenceX.Chat.Installer.wixproj'
$frontendNormalized = $Frontend.ToLowerInvariant()
$primaryExecutable = Resolve-PrimaryExecutableName -FrontendName $frontendNormalized

if ([string]::IsNullOrWhiteSpace($PortableOutDir)) {
    $PortableOutDir = Join-Path $script:RepoRoot ("Artifacts\Portable\{0}" -f $Runtime)
}
if ([string]::IsNullOrWhiteSpace($BundleName)) {
    $BundleName = "IntelligenceX.Chat-Portable-$Runtime"
}

if ([string]::IsNullOrWhiteSpace($PayloadDir)) {
    Write-Header 'Prepare Installer Payload'
    $packageArgs = @{
        Frontend = $frontendNormalized
        Runtime = $Runtime
        Configuration = $Configuration
        Framework = $Framework
        AppFramework = $AppFramework
        PluginMode = 'all'
        IncludePrivateToolPacks = $true
        OutDir = $PortableOutDir
        BundleName = $BundleName
        ClearOut = $true
        Zip = $false
    }
    if ($IncludeService) {
        $packageArgs['IncludeService'] = $true
    }
    if ($IncludeSymbols) {
        $packageArgs['IncludeSymbols'] = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
        $packageArgs['TestimoXRoot'] = $TestimoXRoot
    }
    & $packagePortableScript @packageArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Portable payload packaging failed with exit code $LASTEXITCODE."
    }
    $PayloadDir = Join-Path $PortableOutDir $BundleName
}

if (-not (Test-Path $PayloadDir)) {
    throw "Payload directory does not exist: $PayloadDir"
}
if (-not (Test-Path $installerProject)) {
    throw "Installer project not found: $installerProject"
}

$payloadRoot = [System.IO.Path]::GetFullPath($PayloadDir)
$primaryExePath = Join-Path $payloadRoot $primaryExecutable
if (-not (Test-Path $primaryExePath)) {
    throw "Primary executable missing from payload: $primaryExePath"
}

if ([string]::IsNullOrWhiteSpace($ProductVersion)) {
    $info = (Get-Item $primaryExePath).VersionInfo
    $candidate = if ($info.ProductVersion) { $info.ProductVersion } else { $info.FileVersion }
    $ProductVersion = Convert-ToMsiVersion -RawVersion $candidate
} else {
    $ProductVersion = Convert-ToMsiVersion -RawVersion $ProductVersion
}

$msiRoot = Join-Path $script:RepoRoot ("Artifacts\Installer\IntelligenceX.Chat\{0}" -f $Runtime)
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $msiRoot 'Output'
}
New-Item -ItemType Directory -Path $msiRoot -Force | Out-Null
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$platform = if ($Runtime -eq 'win-arm64') { 'arm64' } else { 'x64' }
$outDirWithSlash = [System.IO.Path]::GetFullPath($OutDir)
if (-not $outDirWithSlash.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
    $outDirWithSlash += [System.IO.Path]::DirectorySeparatorChar
}

$junctionPath = $null
try {
    $junctionPath = New-ShortPayloadJunction -TargetPath $payloadRoot
    $payloadForBuild = [System.IO.Path]::GetFullPath($junctionPath)

    $harvestPath = Join-Path $msiRoot 'Harvest.wxs'
    Write-Header 'Harvest Payload'
    Write-Step "Payload: $payloadRoot"
    Write-Step "Build payload alias: $payloadForBuild"
    Write-Step "Harvest file: $harvestPath"
    $harvestExcludes = @(
        (Join-Path $payloadForBuild 'run-chat.ps1'),
        (Join-Path $payloadForBuild 'run-chat.cmd'),
        (Join-Path $payloadForBuild 'README.md'),
        (Join-Path $payloadForBuild 'portable-bundle.json'),
        (Join-Path $payloadForBuild 'createdump.exe')
    )
    Write-HarvestWxs -PayloadRoot $payloadForBuild -OutputPath $harvestPath -ExcludeFiles $harvestExcludes
    Test-HarvestPayloadManifest -HarvestPath $harvestPath

    $manifest = [ordered]@{
        schemaVersion = 1
        frontend = $frontendNormalized
        runtime = $Runtime
        configuration = $Configuration
        framework = $Framework
        appFramework = $AppFramework
        primaryExecutable = $primaryExecutable
        payloadDir = $payloadRoot
        payloadAliasDir = $payloadForBuild
        primaryExePath = $primaryExePath
        productName = $ProductName
        manufacturer = $Manufacturer
        productVersion = $ProductVersion
        includeSymbols = [bool] $IncludeSymbols
        upgradeCode = $UpgradeCode
        createdUtc = (Get-Date).ToUniversalTime().ToString('o')
    }
    $manifestPath = Join-Path $msiRoot 'installer-manifest.json'
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding UTF8

    Write-Header 'Build MSI'
    Write-Step "Runtime: $Runtime"
    Write-Step "Platform: $platform"
    Write-Step "Output: $outDirWithSlash"

    $buildArgs = @(
        'build',
        $installerProject,
        '-c',
        $Configuration,
        "-p:Platform=$platform",
        "-p:PayloadDir=$payloadForBuild",
        "-p:ProductName=$ProductName",
        "-p:Manufacturer=$Manufacturer",
        "-p:ProductVersion=$ProductVersion",
        "-p:UpgradeCode=$UpgradeCode",
        "-p:PrimaryExe=$primaryExecutable",
        "-p:HarvestFile=$harvestPath",
        "-p:OutputPath=$outDirWithSlash"
    )
    Invoke-DotNet -Args $buildArgs -WorkingDirectory $script:RepoRoot

    $msi = Get-ChildItem -Path $OutDir -Filter '*.msi' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $msi) {
        throw "MSI was not generated in output directory: $OutDir"
    }

    if ($Sign) {
        if (-not $SignThumbprint -and $UseTestimoXSignThumbprintFallback) {
            $resolvedThumbprint = Resolve-TestimoXDefaultSignThumbprint -RepoRoot $script:RepoRoot
            if ($resolvedThumbprint) {
                $SignThumbprint = $resolvedThumbprint
                Write-Warn 'Using default TestimoX signing thumbprint fallback from sibling repo.'
            } else {
                Write-Warn 'No TestimoX signing thumbprint fallback found; MSI signing will rely on subject name or local cert auto-selection.'
            }
        }

        $resolvedSignTool = Resolve-SignToolPath -Path $SignToolPath
        if (-not $resolvedSignTool) {
            throw "SignTool not found. Install Windows SDK or pass -SignToolPath."
        }

        Write-Header 'Sign MSI'
        Write-Step "MSI: $($msi.FullName)"
        $signArgs = @('sign', '/fd', 'sha256', '/v')
        if ($SignThumbprint) { $signArgs += @('/sha1', $SignThumbprint) }
        if ($SignSubjectName) { $signArgs += @('/n', $SignSubjectName) }
        if ($SignTimestampUrl) { $signArgs += @('/tr', $SignTimestampUrl, '/td', 'sha256') }
        if ($SignDescription) { $signArgs += @('/d', $SignDescription) }
        if ($SignUrl) { $signArgs += @('/du', $SignUrl) }
        if ($SignCsp) { $signArgs += @('/csp', $SignCsp) }
        if ($SignKeyContainer) { $signArgs += @('/kc', $SignKeyContainer) }
        & $resolvedSignTool @signArgs $msi.FullName
        if ($LASTEXITCODE -ne 0) {
            throw "MSI signing failed with exit code $LASTEXITCODE"
        }
    }

    Write-Ok ("MSI ready: {0}" -f $msi.FullName)
} finally {
    if ($junctionPath -and (Test-Path $junctionPath)) {
        Remove-Item -Force $junctionPath -ErrorAction SilentlyContinue
    }
}
