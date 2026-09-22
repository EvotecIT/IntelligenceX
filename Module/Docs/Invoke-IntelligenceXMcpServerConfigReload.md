---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Invoke-IntelligenceXMcpServerConfigReload
## SYNOPSIS
Reloads MCP server configuration in the running app-server.

Refreshes MCP configuration after file changes so newly added servers, tools, and auth settings
are visible without restarting the app-server process.

## SYNTAX
### __AllParameterSets
```powershell
Invoke-IntelligenceXMcpServerConfigReload [-Client <IntelligenceXClient>] [<CommonParameters>]
```

## DESCRIPTION
Reloads MCP server configuration in the running app-server.

Refreshes MCP configuration after file changes so newly added servers, tools, and auth settings
are visible without restarting the app-server process.

## EXAMPLES

### EXAMPLE 1
```powershell
Invoke-IntelligenceXMcpServerConfigReload -Client 'Value'
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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `IntelligenceX.OpenAI.IntelligenceXClient`

## OUTPUTS

- `None`

## RELATED LINKS

- None
