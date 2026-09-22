---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Disconnect-IntelligenceX
## SYNOPSIS
Disconnects the active IntelligenceX client and clears local session context.

Releases app-server/native client resources, clears diagnostics subscriptions, and resets
default thread/initialization state. If no client is active, the cmdlet exits without error.

## SYNTAX
### __AllParameterSets
```powershell
Disconnect-IntelligenceX [-Client <IntelligenceXClient>] [<CommonParameters>]
```

## DESCRIPTION
Disconnects the active IntelligenceX client and clears local session context.

Releases app-server/native client resources, clears diagnostics subscriptions, and resets
default thread/initialization state. If no client is active, the cmdlet exits without error.

## EXAMPLES

### EXAMPLE 1
```powershell
Disconnect-IntelligenceX -Client 'Value'
```


## PARAMETERS

### -Client
Client instance to disconnect. Defaults to the active client.

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
