---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Get-IntelligenceXMcpServerStatus
## SYNOPSIS
Lists configured MCP servers with auth, tool, and resource status.

Queries the app-server MCP registry and returns one page of server status data. The result includes
Servers (current page) and NextCursor (for pagination).

## SYNTAX
### __AllParameterSets
```powershell
Get-IntelligenceXMcpServerStatus [-Client <IntelligenceXClient>] [-Cursor <string>] [-Limit <int>] [-Raw] [<CommonParameters>]
```

## DESCRIPTION
Lists configured MCP servers with auth, tool, and resource status.

Queries the app-server MCP registry and returns one page of server status data. The result includes
Servers (current page) and NextCursor (for pagination).

## EXAMPLES

### EXAMPLE 1
```powershell
Get-IntelligenceXMcpServerStatus -Client 'Value'
```


## PARAMETERS

### -Client
App-server client instance to query. Defaults to the active client.

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

### -Cursor
Pagination cursor from a previous response (NextCursor).

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

### -Limit
Maximum number of server entries to return in this page.

```yaml
Type: Nullable`1
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Raw
Returns the raw JSON-RPC payload instead of typed models.

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

- `IntelligenceX.OpenAI.AppServer.Models.McpServerStatusListResult`
- `IntelligenceX.Json.JsonValue`

## RELATED LINKS

- None
