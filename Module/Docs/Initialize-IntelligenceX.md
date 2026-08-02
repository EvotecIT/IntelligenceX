---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Initialize-IntelligenceX
## SYNOPSIS
Initializes the client handshake with the app-server.

Sends client identity metadata (name, title, version) to app-server. Some flows require
initialization before login, chat, or review operations.

## SYNTAX
### __AllParameterSets
```powershell
Initialize-IntelligenceX -Name <string> -Title <string> -Version <string> [-Client <IntelligenceXClient>] [<CommonParameters>]
```

## DESCRIPTION
Initializes the client handshake with the app-server.

Sends client identity metadata (name, title, version) to app-server. Some flows require
initialization before login, chat, or review operations.

## EXAMPLES

### EXAMPLE 1
```powershell
Initialize-IntelligenceX -Name 'Name' -Title 'Value' -Version '1.0.0'
```


## PARAMETERS

### -Client
Client instance to initialize. Defaults to the active client.

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

### -Name
Client identifier sent to the app-server (machine-friendly).

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

### -Title
Client display title sent to the app-server (human-friendly).

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

### -Version
Client version sent to the app-server for telemetry/capability context.

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

- `None`

## RELATED LINKS

- None
