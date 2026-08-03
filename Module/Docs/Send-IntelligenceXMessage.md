---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Send-IntelligenceXMessage
## SYNOPSIS
Sends a message to a thread and starts a turn.

Queues a user message on an existing thread. Returns the created turn so you can
poll for output with Get-IntelligenceXTurnOutput.

## SYNTAX
### __AllParameterSets
```powershell
Send-IntelligenceXMessage -ThreadId <string> -Text <string> [-Client <IntelligenceXClient>] [-Model <string>] [-CurrentDirectory <string>] [-ApprovalPolicy <string>] [-SandboxType <string>] [-NetworkAccess] [-WritableRoot <string[]>] [-Raw] [<CommonParameters>]
```

## DESCRIPTION
Sends a message to a thread and starts a turn.

Queues a user message on an existing thread. Returns the created turn so you can
poll for output with Get-IntelligenceXTurnOutput.

## EXAMPLES

### EXAMPLE 1
```powershell
Send-IntelligenceXMessage -ThreadId 'Value' -Text 'Value'
```


## PARAMETERS

### -ApprovalPolicy
Approval policy name to use.

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

### -CurrentDirectory
Working directory to pass to the app-server.

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

### -Model
Optional model override.

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

### -NetworkAccess
Enable network access for sandboxed runs.

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

### -Raw
Return raw JSON response.

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

### -SandboxType
Sandbox type (for example 'workspace' or 'danger-full-access').

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

### -Text
Message text to send.

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

### -ThreadId
Thread identifier to send the message to.

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

### -WritableRoot
Writable root paths for sandboxed runs.

```yaml
Type: String[]
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

- `IntelligenceX.OpenAI.AppServer.Models.TurnInfo`
- `IntelligenceX.Json.JsonValue`

## RELATED LINKS

- None
