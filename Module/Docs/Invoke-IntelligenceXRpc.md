---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Invoke-IntelligenceXRpc
## SYNOPSIS
Invokes a raw JSON-RPC method directly against app-server.

Low-level escape hatch for advanced scenarios not covered by high-level cmdlets.
Parameters are converted from PowerShell objects/hashtables to JSON payloads.

## SYNTAX
### __AllParameterSets
```powershell
Invoke-IntelligenceXRpc -Method <string> [-Client <IntelligenceXClient>] [-Params <Object>] [<CommonParameters>]
```

## DESCRIPTION
Invokes a raw JSON-RPC method directly against app-server.

Low-level escape hatch for advanced scenarios not covered by high-level cmdlets.
Parameters are converted from PowerShell objects/hashtables to JSON payloads.

## EXAMPLES

### EXAMPLE 1
```powershell
Invoke-IntelligenceXRpc -Method 'Value'
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
JSON-RPC method name (for example thread/list).

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Params
Optional method parameters supplied as a PowerShell object/hashtable.

```yaml
Type: Object
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

- `IntelligenceX.Json.JsonValue`

## RELATED LINKS

- None
