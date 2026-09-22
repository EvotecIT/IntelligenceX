---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Stop-IntelligenceXTurn
## SYNOPSIS
Interrupts a running turn for a thread.

Requests cancellation for an in-progress turn. Use this for long-running responses,
accidental prompts, or when you need to restart execution with different instructions.

## SYNTAX
### __AllParameterSets
```powershell
Stop-IntelligenceXTurn -ThreadId <string> -TurnId <string> [-Client <IntelligenceXClient>] [<CommonParameters>]
```

## DESCRIPTION
Interrupts a running turn for a thread.

Requests cancellation for an in-progress turn. Use this for long-running responses,
accidental prompts, or when you need to restart execution with different instructions.

## EXAMPLES

### EXAMPLE 1
```powershell
Stop-IntelligenceXTurn -ThreadId 'Value' -TurnId 'Value'
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

### -ThreadId
Identifier of the thread that owns the running turn.

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

### -TurnId
Identifier of the turn to interrupt.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `IntelligenceX.OpenAI.IntelligenceXClient`

## OUTPUTS

- `None`

## RELATED LINKS

- None
