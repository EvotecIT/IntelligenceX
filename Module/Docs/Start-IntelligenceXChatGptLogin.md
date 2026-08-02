---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Start-IntelligenceXChatGptLogin
## SYNOPSIS
Starts the ChatGPT login flow and returns the authorization URL.

Opens the browser to complete the ChatGPT OAuth flow. Pair with Wait-IntelligenceXLogin
to poll for completion and store the resulting credentials.

## SYNTAX
### __AllParameterSets
```powershell
Start-IntelligenceXChatGptLogin [-Client <IntelligenceXClient>] [-Raw] [<CommonParameters>]
```

## DESCRIPTION
Starts the ChatGPT login flow and returns the authorization URL.

Opens the browser to complete the ChatGPT OAuth flow. Pair with Wait-IntelligenceXLogin
to poll for completion and store the resulting credentials.

## EXAMPLES

### EXAMPLE 1
```powershell
Start-IntelligenceXChatGptLogin -Client 'Value'
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
Returns the raw JSON-RPC payload instead of typed login data.

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

- `IntelligenceX.OpenAI.AppServer.Models.ChatGptLoginStart`
- `IntelligenceX.Json.JsonValue`

## RELATED LINKS

- None
