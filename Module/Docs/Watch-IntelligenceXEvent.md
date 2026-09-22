---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Watch-IntelligenceXEvent
## SYNOPSIS
Watches JSON-RPC notifications from the app-server.

Streams notification events until cancelled. Use method filters to observe specific
protocol events such as turn progress, login completion, or status changes.

## SYNTAX
### __AllParameterSets
```powershell
Watch-IntelligenceXEvent [-Client <IntelligenceXClient>] [-Method <string[]>] [<CommonParameters>]
```

## DESCRIPTION
Watches JSON-RPC notifications from the app-server.

Streams notification events until cancelled. Use method filters to observe specific
protocol events such as turn progress, login completion, or status changes.

## EXAMPLES

### EXAMPLE 1
```powershell
Watch-IntelligenceXEvent -Client 'Value'
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

### -Method
Optional JSON-RPC method filter list. Matching is case-insensitive.

```yaml
Type: String[]
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

- `IntelligenceX.PowerShell.RpcNotificationRecord`: Represents a JSON-RPC notification record for PowerShell consumers.

## RELATED LINKS

- None
