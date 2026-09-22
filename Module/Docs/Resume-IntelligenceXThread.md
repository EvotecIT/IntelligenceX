---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Resume-IntelligenceXThread
## SYNOPSIS
Resumes an existing thread so new messages can be sent to it.

Loads thread context back into the active app-server session and returns thread metadata.
Useful after reconnecting or when switching between multiple threads.

## SYNTAX
### __AllParameterSets
```powershell
Resume-IntelligenceXThread -ThreadId <string> [-Client <IntelligenceXClient>] [-Raw] [<CommonParameters>]
```

## DESCRIPTION
Resumes an existing thread so new messages can be sent to it.

Loads thread context back into the active app-server session and returns thread metadata.
Useful after reconnecting or when switching between multiple threads.

## EXAMPLES

### EXAMPLE 1
```powershell
Resume-IntelligenceXThread -ThreadId 'Value'
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
Identifier of the thread to resume.

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
