---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Get-IntelligenceXCollaborationMode
## SYNOPSIS
Lists collaboration modes available in the current app-server runtime.

Use this cmdlet to discover valid collaboration mode values before setting them in
configuration or passing them to review/chat workflows.

## SYNTAX
### __AllParameterSets
```powershell
Get-IntelligenceXCollaborationMode [-Client <IntelligenceXClient>] [-Raw] [<CommonParameters>]
```

## DESCRIPTION
Lists collaboration modes available in the current app-server runtime.

Use this cmdlet to discover valid collaboration mode values before setting them in
configuration or passing them to review/chat workflows.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-IntelligenceXCollaborationMode -Client 'Value'
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
Returns the raw JSON-RPC payload instead of typed mode models.

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

- `IntelligenceX.OpenAI.AppServer.Models.CollaborationModeListResult`
- `IntelligenceX.Json.JsonValue`

## RELATED LINKS

- None
