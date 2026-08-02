---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Get-IntelligenceXHealth
## SYNOPSIS
Runs health checks for OpenAI app-server and optional Copilot CLI.

Returns health status for the active IntelligenceX client and, optionally, a Copilot CLI
instance using explicit or config-derived options.

## SYNTAX
### __AllParameterSets
```powershell
Get-IntelligenceXHealth [-Client <IntelligenceXClient>] [-Copilot] [-NoConfig] [-CopilotCliPath <string>] [-CopilotCliUrl <string>] [-CopilotWorkingDirectory <string>] [-CopilotAutoInstall] [-CopilotInstallMethod <CopilotCliInstallMethod>] [-CopilotInstallPrerelease] [<CommonParameters>]
```

## DESCRIPTION
Runs health checks for OpenAI app-server and optional Copilot CLI.

Returns health status for the active IntelligenceX client and, optionally, a Copilot CLI
instance using explicit or config-derived options.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-IntelligenceXHealth -CopilotCliPath 'C:\Path'
```


## PARAMETERS

### -Client
OpenAI/app-server client instance. Defaults to the active client.

```yaml
Type: IntelligenceXClient
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -Copilot
Run a Copilot CLI health check.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CopilotAutoInstall
Auto-install Copilot CLI if missing.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CopilotCliPath
Copilot CLI path.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CopilotCliUrl
Copilot CLI URL (host:port).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CopilotInstallMethod
Copilot auto-install method to use when -CopilotAutoInstall is set.

```yaml
Type: CopilotCliInstallMethod
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Auto, Winget, Homebrew, Npm, Script

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CopilotInstallPrerelease
Copilot auto-install prerelease.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CopilotWorkingDirectory
Copilot CLI working directory.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NoConfig
Ignore .intelligencex/config.json overrides.

```yaml
Type: SwitchParameter
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `IntelligenceX.OpenAI.IntelligenceXClient`

## OUTPUTS

- `IntelligenceX.PowerShell.HealthReportRecord`: Represents a combined health report for OpenAI and Copilot providers.

## RELATED LINKS

- None
