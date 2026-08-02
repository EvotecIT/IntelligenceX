---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Connect-IntelligenceX
## SYNOPSIS
Connects to IntelligenceX (native or app-server) and returns a client instance.

Use Native for ChatGPT OAuth login (no local Codex binary). Use AppServer to talk to a locally
running Codex app-server process. The cmdlet sets the active default client, so subsequent cmdlets can omit -Client.

## SYNTAX
### __AllParameterSets
```powershell
Connect-IntelligenceX [-Transport <OpenAITransportKind>] [-ExecutablePath <string>] [-Arguments <string>] [-WorkingDirectory <string>] [-Diagnostics] [-NoConfig] [-OpenAIAccountId <string>] [<CommonParameters>]
```

## DESCRIPTION
Connects to IntelligenceX (native or app-server) and returns a client instance.

Use Native for ChatGPT OAuth login (no local Codex binary). Use AppServer to talk to a locally
running Codex app-server process. The cmdlet sets the active default client, so subsequent cmdlets can omit -Client.

## EXAMPLES

### EXAMPLE 1
```powershell
Connect-IntelligenceX -ExecutablePath 'C:\Path'
```


## PARAMETERS

### -Arguments
Arguments to pass to the app-server. Defaults to 'app-server'.

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

### -Diagnostics
Enable diagnostics output (RPC calls, login events, stderr).

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

### -ExecutablePath
Path to the codex executable. Defaults to 'codex' on PATH.

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

### -NoConfig
Ignore .intelligencex/config.json overrides.

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

### -OpenAIAccountId
Optional ChatGPT account id to use when multiple auth bundles are present.

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

### -Transport
Transport to use (Native or AppServer). Native uses ChatGPT OAuth directly.

```yaml
Type: OpenAITransportKind
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Native, AppServer, CompatibleHttp, CopilotCli

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WorkingDirectory
Working directory for the app-server process.

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

- `None`

## OUTPUTS

- `IntelligenceX.OpenAI.IntelligenceXClient`

## RELATED LINKS

- None
