---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Request-IntelligenceXUserInput
## SYNOPSIS
Requests user input through the app-server.

Prompts for one to three questions and returns collected answers in order.
Useful for interactive script checkpoints that need explicit user confirmation or values.

## SYNTAX
### __AllParameterSets
```powershell
Request-IntelligenceXUserInput -Questions <string[]> [-Client <IntelligenceXClient>] [-Raw] [<CommonParameters>]
```

## DESCRIPTION
Requests user input through the app-server.

Prompts for one to three questions and returns collected answers in order.
Useful for interactive script checkpoints that need explicit user confirmation or values.

## EXAMPLES

### EXAMPLE 1
```powershell
Request-IntelligenceXUserInput -Questions @('Value')
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

### -Questions
Questions to ask (minimum 1, maximum 3).

```yaml
Type: String[]
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Raw
Returns the raw JSON-RPC payload instead of typed response data.

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

- `IntelligenceX.OpenAI.AppServer.Models.UserInputResponse`
- `IntelligenceX.Json.JsonValue`

## RELATED LINKS

- None
