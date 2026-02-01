#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build and serve the IntelligenceX website.

.PARAMETER Serve
    Start the development server after building.

.PARAMETER Port
    Server port (default: 8080).

.EXAMPLE
    ./build.ps1
    ./build.ps1 -Serve
    ./build.ps1 -Serve -Port 3000
#>

param(
    [switch]$Serve,
    [int]$Port = 8080
)

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot

# Resolve PowerForge.Web.Cli executable
$PowerForge = $null

# 1. Check if powerforge-web is on PATH (global tool / published)
$PowerForge = Get-Command powerforge-web -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source

# 2. Fallback to local PSPublishModule build
if (-not $PowerForge) {
    $localPaths = @(
        'C:\Support\GitHub\PSPublishModule\PowerForge.Web.Cli\bin\Release\net10.0\PowerForge.Web.Cli.exe'
        'C:\Support\GitHub\PSPublishModule\PowerForge.Web.Cli\bin\Release\net8.0\PowerForge.Web.Cli.exe'
        'C:\Support\GitHub\PSPublishModule\PowerForge.Web.Cli\bin\Debug\net10.0\PowerForge.Web.Cli.exe'
        'C:\Support\GitHub\PSPublishModule\PowerForge.Web.Cli\bin\Debug\net8.0\PowerForge.Web.Cli.exe'
    )
    foreach ($p in $localPaths) {
        if (Test-Path $p) { $PowerForge = $p; break }
    }
}

if (-not $PowerForge) {
    Write-Error 'PowerForge.Web.Cli not found. Build PSPublishModule first or install the global tool.'
    exit 1
}

Write-Host "Using: $PowerForge" -ForegroundColor DarkGray

try {
    if ($Serve) {
        Write-Host "Starting dev server on http://localhost:$Port ..." -ForegroundColor Cyan
        & $PowerForge serve --config site.json --out _site --port $Port
    } else {
        Write-Host 'Building website...' -ForegroundColor Cyan
        & $PowerForge pipeline --config pipeline.json
        if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)" }
        Write-Host 'Build complete -> _site/' -ForegroundColor Green
    }
} finally {
    Pop-Location
}
