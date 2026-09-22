---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Wait-IntelligenceXLogin
## SYNOPSIS
Waits for the login flow to complete.

Polls the app-server for login completion. Use after Start-IntelligenceXChatGptLogin.

## SYNTAX
### __AllParameterSets
```powershell
Wait-IntelligenceXLogin [-Client <IntelligenceXClient>] [-LoginId <string>] [-TimeoutSeconds <int>] [<CommonParameters>]
```

## DESCRIPTION
Waits for the login flow to complete.

Polls the app-server for login completion. Use after Start-IntelligenceXChatGptLogin.

## EXAMPLES

### EXAMPLE 1
```powershell
Wait-IntelligenceXLogin -Client 'Value'
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

### -LoginId
Optional login identifier to wait for.

```yaml
Type: String
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TimeoutSeconds
Maximum wait time in seconds before cancellation.

```yaml
Type: Int32
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

- `None`

## RELATED LINKS

- None
