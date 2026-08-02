---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Get-IntelligenceXConfig
## SYNOPSIS
Reads the effective app-server configuration with layer metadata.

Returns merged config values plus metadata that explains where values come from
(for example defaults, workspace files, or environment overrides).

## SYNTAX
### __AllParameterSets
```powershell
Get-IntelligenceXConfig [-Client <IntelligenceXClient>] [-Raw] [<CommonParameters>]
```

## DESCRIPTION
Reads the effective app-server configuration with layer metadata.

Returns merged config values plus metadata that explains where values come from
(for example defaults, workspace files, or environment overrides).

## EXAMPLES

### EXAMPLE 1
```powershell
Get-IntelligenceXConfig -Client 'Value'
```


## PARAMETERS

### -Client
App-server client instance to query. Defaults to the active client.

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

### -Raw
Returns the raw JSON-RPC payload instead of typed config models.

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

- `IntelligenceX.OpenAI.AppServer.Models.ConfigReadResult`
- `IntelligenceX.Json.JsonValue`

## RELATED LINKS

- None
