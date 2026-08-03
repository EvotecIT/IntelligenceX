---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Get-IntelligenceXLoadedThread
## SYNOPSIS
Lists threads currently loaded in the app-server process.

Returns thread ids that are currently active in memory for the running app-server session.
Useful for diagnostics and cleanup scripts.

## SYNTAX
### __AllParameterSets
```powershell
Get-IntelligenceXLoadedThread [-Client <IntelligenceXClient>] [-Raw] [<CommonParameters>]
```

## DESCRIPTION
Lists threads currently loaded in the app-server process.

Returns thread ids that are currently active in memory for the running app-server session.
Useful for diagnostics and cleanup scripts.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-IntelligenceXLoadedThread -Client 'Value'
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
Returns the raw JSON-RPC payload instead of typed thread-id models.

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

- `IntelligenceX.OpenAI.AppServer.Models.ThreadIdListResult`
- `IntelligenceX.Json.JsonValue`

## RELATED LINKS

- None
