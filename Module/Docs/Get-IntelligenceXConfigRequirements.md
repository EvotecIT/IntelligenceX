---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Get-IntelligenceXConfigRequirements
## SYNOPSIS
Reads server-defined constraints for supported configuration values.

Returns allowed values for key settings (for example approval policy and sandbox mode)
so scripts can validate config before writing changes.

## SYNTAX
### __AllParameterSets
```powershell
Get-IntelligenceXConfigRequirements [-Client <IntelligenceXClient>] [-Raw] [<CommonParameters>]
```

## DESCRIPTION
Reads server-defined constraints for supported configuration values.

Returns allowed values for key settings (for example approval policy and sandbox mode)
so scripts can validate config before writing changes.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-IntelligenceXConfigRequirements -Client 'Value'
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
Returns the raw JSON-RPC payload instead of typed requirements models.

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

- `IntelligenceX.OpenAI.AppServer.Models.ConfigRequirementsReadResult`
- `IntelligenceX.Json.JsonValue`

## RELATED LINKS

- None
