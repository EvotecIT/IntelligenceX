---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# New-IntelligenceXThreadFork
## SYNOPSIS
Creates a new thread fork from an existing thread's history.

Use this when you want to branch a conversation without mutating the original thread.
The fork inherits prior context and gets a new thread id.

## SYNTAX
### __AllParameterSets
```powershell
New-IntelligenceXThreadFork -ThreadId <string> [-Client <IntelligenceXClient>] [-Raw] [<CommonParameters>]
```

## DESCRIPTION
Creates a new thread fork from an existing thread's history.

Use this when you want to branch a conversation without mutating the original thread.
The fork inherits prior context and gets a new thread id.

## EXAMPLES

### EXAMPLE 1
```powershell
New-IntelligenceXThreadFork -ThreadId 'Value'
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
Returns the raw JSON-RPC payload instead of typed thread info.

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

### -ThreadId
Identifier of the source thread to fork.

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

- `IntelligenceX.OpenAI.AppServer.Models.ThreadInfo`
- `IntelligenceX.Json.JsonValue`

## RELATED LINKS

- None
