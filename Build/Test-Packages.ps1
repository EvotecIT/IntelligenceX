[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory
)

$ErrorActionPreference = 'Stop'

function Get-PackageManifest {
    param(
        [Parameter(Mandatory)]
        [string] $PackagePath
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $nuspecEntry = $archive.Entries | Where-Object FullName -Like '*.nuspec' | Select-Object -First 1
        if ($null -eq $nuspecEntry) {
            throw "Package '$PackagePath' does not contain a NuSpec manifest."
        }

        $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
        try {
            [xml] $nuspec = $reader.ReadToEnd()
        } finally {
            $reader.Dispose()
        }

        [pscustomobject]@{
            Id                   = [string] $nuspec.package.metadata.id
            Version              = [string] $nuspec.package.metadata.version
            Readme               = [string] $nuspec.package.metadata.readme
            Dependencies         = @($nuspec.package.metadata.dependencies.group.dependency)
            DependencyFrameworks = @($nuspec.package.metadata.dependencies.group | ForEach-Object targetFramework)
            Entries              = @($archive.Entries.FullName)
        }
    } finally {
        $archive.Dispose()
    }
}

function Assert-PackageEntry {
    param(
        [Parameter(Mandatory)]
        [object] $Manifest,
        [Parameter(Mandatory)]
        [string] $Entry
    )

    if ($Manifest.Entries -notcontains $Entry) {
        throw "Package '$($Manifest.Id)' is missing '$Entry'."
    }
}

function Assert-PackageMissingEntry {
    param(
        [Parameter(Mandatory)]
        [object] $Manifest,
        [Parameter(Mandatory)]
        [string] $Entry
    )

    if ($Manifest.Entries -contains $Entry) {
        throw "Package '$($Manifest.Id)' must not contain '$Entry'."
    }
}

$resolvedPackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$packageFiles = @(Get-ChildItem -LiteralPath $resolvedPackageDirectory -File -Filter '*.nupkg' |
    Where-Object Name -NotLike '*.symbols.nupkg')
$manifests = @($packageFiles | ForEach-Object { Get-PackageManifest -PackagePath $_.FullName })

$sdk = @($manifests | Where-Object Id -EQ 'IntelligenceX')
$storage = @($manifests | Where-Object Id -EQ 'IntelligenceX.Storage.SQLite')
if ($sdk.Count -ne 1 -or $storage.Count -ne 1) {
    throw "Expected exactly one IntelligenceX and one IntelligenceX.Storage.SQLite package in '$resolvedPackageDirectory'."
}
if ($sdk[0].Version -ne $storage[0].Version) {
    throw "Package versions differ: IntelligenceX $($sdk[0].Version), storage $($storage[0].Version)."
}
$sdkSymbolsPath = Join-Path $resolvedPackageDirectory "IntelligenceX.$($sdk[0].Version).snupkg"
$storageSymbolsPath = Join-Path $resolvedPackageDirectory "IntelligenceX.Storage.SQLite.$($sdk[0].Version).snupkg"
foreach ($symbolPackage in $sdkSymbolsPath, $storageSymbolsPath) {
    if (-not (Test-Path -LiteralPath $symbolPackage -PathType Leaf)) {
        throw "Package artifact '$symbolPackage' is missing."
    }
}
$sdkSymbols = Get-PackageManifest -PackagePath $sdkSymbolsPath
$storageSymbols = Get-PackageManifest -PackagePath $storageSymbolsPath

foreach ($framework in 'netstandard2.0', 'net472', 'net8.0', 'net10.0') {
    Assert-PackageEntry -Manifest $sdk[0] -Entry "lib/$framework/IntelligenceX.dll"
    Assert-PackageEntry -Manifest $sdk[0] -Entry "lib/$framework/IntelligenceX.xml"
    Assert-PackageEntry -Manifest $sdkSymbols -Entry "lib/$framework/IntelligenceX.pdb"
}
foreach ($framework in 'net8.0', 'net10.0') {
    Assert-PackageEntry -Manifest $storage[0] -Entry "lib/$framework/IntelligenceX.Storage.SQLite.dll"
    Assert-PackageEntry -Manifest $storage[0] -Entry "lib/$framework/IntelligenceX.Storage.SQLite.xml"
    Assert-PackageEntry -Manifest $storageSymbols -Entry "lib/$framework/IntelligenceX.Storage.SQLite.pdb"
}
Assert-PackageMissingEntry -Manifest $storage[0] -Entry 'lib/net472/IntelligenceX.Storage.SQLite.dll'
Assert-PackageMissingEntry -Manifest $storage[0] -Entry 'ref/net472/IntelligenceX.Storage.SQLite.dll'
Assert-PackageMissingEntry -Manifest $storage[0] -Entry 'runtimes/win-x64/lib/net472/IntelligenceX.Storage.SQLite.dll'
Assert-PackageMissingEntry -Manifest $storageSymbols -Entry 'lib/net472/IntelligenceX.Storage.SQLite.pdb'
if ($storage[0].DependencyFrameworks -contains '.NETFramework4.7.2') {
    throw 'The storage package must not advertise its unsupported net472 dependency graph.'
}
Assert-PackageEntry -Manifest $sdk[0] -Entry 'README.md'
Assert-PackageEntry -Manifest $storage[0] -Entry 'README.md'
Assert-PackageEntry -Manifest $sdk[0] -Entry 'icon.png'
Assert-PackageEntry -Manifest $storage[0] -Entry 'icon.png'

$sdkDependencyIds = @($sdk[0].Dependencies | ForEach-Object id | Sort-Object -Unique)
if ($sdkDependencyIds -contains 'IntelligenceX.Shared') {
    throw 'The main SDK still depends on the retired IntelligenceX.Shared package.'
}
$storageDependencyIds = @($storage[0].Dependencies | ForEach-Object id | Sort-Object -Unique)
foreach ($requiredDependency in 'DBAClientX.SQLite', 'IntelligenceX') {
    if ($storageDependencyIds -notcontains $requiredDependency) {
        throw "The storage package is missing dependency '$requiredDependency'."
    }
}
$storageFrameworks = @($storage[0].DependencyFrameworks | Sort-Object -Unique)
if (($storageFrameworks -join ',') -ne 'net10.0,net8.0') {
    throw "The storage package targets an unexpected framework set: $($storageFrameworks -join ', ')."
}
$dbaDependency = @($storage[0].Dependencies | Where-Object id -EQ 'DBAClientX.SQLite')
if ($dbaDependency.Count -ne 2) {
    throw "Expected one DBAClientX.SQLite dependency in each supported storage framework group."
}
foreach ($dependency in $dbaDependency) {
    $includedAssets = @(([string] $dependency.include).Split(',', [System.StringSplitOptions]::RemoveEmptyEntries))
    if ($includedAssets -contains 'Compile') {
        throw 'DBAClientX.SQLite must remain an implementation dependency rather than leaking into the storage compile surface.'
    }
}

$smokeSource = Join-Path $PSScriptRoot '..\IntelligenceX.PackageSmoke'
$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('intelligencex-package-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $smokeRoot | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $smokeSource 'IntelligenceX.PackageSmoke.csproj') -Destination $smokeRoot
    Copy-Item -LiteralPath (Join-Path $smokeSource 'Program.cs') -Destination $smokeRoot
    $smokeProject = Join-Path $smokeRoot 'IntelligenceX.PackageSmoke.csproj'
    $packagesRoot = Join-Path $smokeRoot 'packages'
    $nugetConfig = Join-Path $smokeRoot 'NuGet.Config'
    $escapedPackageDirectory = [System.Security.SecurityElement]::Escape($resolvedPackageDirectory)
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="staged" value="$escapedPackageDirectory" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="staged">
      <package pattern="IntelligenceX" />
      <package pattern="IntelligenceX.Storage.SQLite" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $nugetConfig -Encoding utf8

    & dotnet restore $smokeProject --configfile $nugetConfig --packages $packagesRoot --no-http-cache -p:PackageVersion=$($sdk[0].Version) --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Package smoke restore failed with exit code $LASTEXITCODE."
    }

    $stagedPackages = @{
        IntelligenceX = ($packageFiles | Where-Object Name -EQ "IntelligenceX.$($sdk[0].Version).nupkg" | Select-Object -First 1).FullName
        'IntelligenceX.Storage.SQLite' = ($packageFiles | Where-Object Name -EQ "IntelligenceX.Storage.SQLite.$($sdk[0].Version).nupkg" | Select-Object -First 1).FullName
    }
    foreach ($packageId in $stagedPackages.Keys) {
        $normalizedId = $packageId.ToLowerInvariant()
        $restoredPackage = Join-Path $packagesRoot "$normalizedId\$($sdk[0].Version)\$normalizedId.$($sdk[0].Version).nupkg"
        if (-not (Test-Path -LiteralPath $restoredPackage -PathType Leaf) -or
            (Get-FileHash -LiteralPath $restoredPackage -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $stagedPackages[$packageId] -Algorithm SHA256).Hash) {
            throw "Package smoke did not restore the staged '$packageId' artifact byte-for-byte."
        }
    }

    $smokeFrameworks = @('net8.0', 'net10.0')
    if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) {
        $smokeFrameworks += 'net472'
    }
    foreach ($framework in $smokeFrameworks) {
        & dotnet run --project $smokeProject --framework $framework --configuration Release --no-restore -p:PackageVersion=$($sdk[0].Version) --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Package smoke run for $framework failed with exit code $LASTEXITCODE."
        }
    }
} finally {
    if ($smokeRoot.StartsWith([System.IO.Path]::GetTempPath(), [System.StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $smokeRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Validated IntelligenceX $($sdk[0].Version) and IntelligenceX.Storage.SQLite $($storage[0].Version)." -ForegroundColor Green
