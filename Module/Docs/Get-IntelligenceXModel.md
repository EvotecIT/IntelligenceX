---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Get-IntelligenceXModel
## SYNOPSIS
Lists models available for the current transport context.

Returns model metadata for the active client. Raw JSON mode is available only on app-server
transport because native transport returns strongly typed models directly.

## SYNTAX
### __AllParameterSets
```powershell
Get-IntelligenceXModel [-Client <IntelligenceXClient>] [-Raw] [<CommonParameters>]
```

## DESCRIPTION
Lists models available for the current transport context.

Returns model metadata for the active client. Raw JSON mode is available only on app-server
transport because native transport returns strongly typed models directly.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-IntelligenceXModel -Client 'Value'
```


## PARAMETERS

### -Client
Client instance to use. Defaults to the active client.

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
Returns raw JSON-RPC payload (app-server transport only).

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

- `IntelligenceX.OpenAI.AppServer.Models.ModelListResult`
- `IntelligenceX.Json.JsonValue`

## RELATED LINKS

- None
