---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Set-IntelligenceXConfigBatch
## SYNOPSIS
Writes multiple app-server configuration values in one request.

Sends a batch of key/value updates. This is useful for related settings you want to apply together.
Hashtable values are converted to JSON before sending.

## SYNTAX
### __AllParameterSets
```powershell
Set-IntelligenceXConfigBatch -Values <hashtable> [-Client <IntelligenceXClient>] [<CommonParameters>]
```

## DESCRIPTION
Writes multiple app-server configuration values in one request.

Sends a batch of key/value updates. This is useful for related settings you want to apply together.
Hashtable values are converted to JSON before sending.

## EXAMPLES

### EXAMPLE 1
```powershell
Set-IntelligenceXConfigBatch -Values @{}
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

### -Values
Hashtable of configuration key/value pairs to write.

```yaml
Type: Hashtable
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
