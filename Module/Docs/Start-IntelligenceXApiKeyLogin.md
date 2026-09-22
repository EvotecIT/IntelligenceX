---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Start-IntelligenceXApiKeyLogin
## SYNOPSIS
Authenticates the active client with an OpenAI API key.

Stores the API key in the active client for API-based requests. Use only if ChatGPT OAuth
is not desired or available.

## SYNTAX
### __AllParameterSets
```powershell
Start-IntelligenceXApiKeyLogin -ApiKey <string> [-Client <IntelligenceXClient>] [<CommonParameters>]
```

## DESCRIPTION
Authenticates the active client with an OpenAI API key.

Stores the API key in the active client for API-based requests. Use only if ChatGPT OAuth
is not desired or available.

## EXAMPLES

### EXAMPLE 1
```powershell
Start-IntelligenceXApiKeyLogin -ApiKey 'Value'
```


## PARAMETERS

### -ApiKey
OpenAI API key used for authentication.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `IntelligenceX.OpenAI.IntelligenceXClient`

## OUTPUTS

- `None`

## RELATED LINKS

- None
