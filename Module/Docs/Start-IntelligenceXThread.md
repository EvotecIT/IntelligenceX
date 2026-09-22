---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Start-IntelligenceXThread
## SYNOPSIS
Creates a new conversation thread.

Starts a fresh thread on the app-server with a selected model and optional execution
settings like sandbox and approval policy.

## SYNTAX
### __AllParameterSets
```powershell
Start-IntelligenceXThread -Model <string> [-Client <IntelligenceXClient>] [-CurrentDirectory <string>] [-ApprovalPolicy <string>] [-Sandbox <string>] [-Raw] [<CommonParameters>]
```

## DESCRIPTION
Creates a new conversation thread.

Starts a fresh thread on the app-server with a selected model and optional execution
settings like sandbox and approval policy.

## EXAMPLES

### EXAMPLE 1
```powershell
Start-IntelligenceXThread -Model 'Value'
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
Model identifier to use (for example gpt-5.4).

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

### -Sandbox
Sandbox mode to use (for example 'workspace' or 'danger-full-access').

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

- `IntelligenceX.OpenAI.AppServer.Models.ThreadInfo`
- `IntelligenceX.Json.JsonValue`

## RELATED LINKS

- None
