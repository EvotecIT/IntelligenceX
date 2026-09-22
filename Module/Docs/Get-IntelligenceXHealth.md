---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Get-IntelligenceXHealth
## SYNOPSIS
Checks the active IntelligenceX connection and optional native Copilot access.

Copilot checks use direct HTTPS model discovery. No CLI installation or process is required.

## SYNTAX
### __AllParameterSets
```powershell
Get-IntelligenceXHealth [-Client <IntelligenceXClient>] [-Copilot] [-NoConfig] [-CopilotBaseUrl <string>] [<CommonParameters>]
```

## DESCRIPTION
Checks the active IntelligenceX connection and optional native Copilot access.

Copilot checks use direct HTTPS model discovery. No CLI installation or process is required.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-IntelligenceXHealth -Client 'Value'
```


## PARAMETERS

### -Client
Client to check. Defaults to the active client.

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
Also check Copilot model access using native HTTP.

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

### -CopilotBaseUrl
Optional explicitly trusted Copilot HTTPS API root.

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
