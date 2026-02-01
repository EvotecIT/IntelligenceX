---
title: PowerShell Module Overview
description: Binary cmdlets for IntelligenceX automation and scripting
collection: docs
layout: docs
nav.weight: 10
---

# PowerShell Module Overview

The PowerShell module wraps the core library with binary cmdlets targeting net472 and net8.

## Build

```powershell
./Module/Build/Build-Module.ps1
```

## Import and Use

```powershell
Import-Module ./Module/IntelligenceX.psd1 -Force

$client = Connect-IntelligenceX
Initialize-IntelligenceX -Client $client -Name 'IntelligenceX' -Title 'Demo' -Version '0.1.0'

$login = Start-IntelligenceXChatGptLogin -Client $client
Write-Host $login.AuthUrl
Wait-IntelligenceXLogin -Client $client -LoginId $login.LoginId

$thread = Start-IntelligenceXThread -Client $client -Model 'gpt-5.2-codex'
Send-IntelligenceXMessage -Client $client -ThreadId $thread.Id -Text 'Hello from PowerShell'

Disconnect-IntelligenceX -Client $client
```

## Diagnostics

```powershell
Connect-IntelligenceX -Diagnostics
Get-IntelligenceXHealth
```

## Examples

Additional examples are in the `Module/Examples` directory of the repository.
