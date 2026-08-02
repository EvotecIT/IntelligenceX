---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Start-IntelligenceXMcpOAuthLogin
## SYNOPSIS
Starts an MCP OAuth login flow and returns the browser authorization URL.

Use this cmdlet when an MCP server reports OAuth auth status. The response includes a
LoginId and AuthUrl you can open in a browser.

## SYNTAX
### __AllParameterSets
```powershell
Start-IntelligenceXMcpOAuthLogin [-Client <IntelligenceXClient>] [-ServerId <string>] [-ServerName <string>] [-Raw] [<CommonParameters>]
```

## DESCRIPTION
Starts an MCP OAuth login flow and returns the browser authorization URL.

Use this cmdlet when an MCP server reports OAuth auth status. The response includes a
LoginId and AuthUrl you can open in a browser.

## EXAMPLES

### EXAMPLE 1
```powershell
Start-IntelligenceXMcpOAuthLogin -Client 'Value'
```


## PARAMETERS

### -Client
App-server client instance to use. Defaults to the active client.

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
Returns the raw JSON-RPC payload instead of a typed model.

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

### -ServerId
MCP server identifier to start login for.

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

### -ServerName
MCP server name to start login for.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `IntelligenceX.OpenAI.IntelligenceXClient`

## OUTPUTS

- `IntelligenceX.OpenAI.AppServer.Models.McpOauthLoginStart`
- `IntelligenceX.Json.JsonValue`

## RELATED LINKS

- None
