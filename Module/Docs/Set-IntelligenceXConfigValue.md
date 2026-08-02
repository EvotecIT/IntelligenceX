---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Set-IntelligenceXConfigValue
## SYNOPSIS
Writes a single app-server configuration value.

Updates one config key on the app-server. PowerShell values are converted to JSON before sending.
Use Get-IntelligenceXConfig to verify the effective result and layer origin.

## SYNTAX
### __AllParameterSets
```powershell
Set-IntelligenceXConfigValue -Key <string> -Value <Object> [-Client <IntelligenceXClient>] [<CommonParameters>]
```

## DESCRIPTION
Writes a single app-server configuration value.

Updates one config key on the app-server. PowerShell values are converted to JSON before sending.
Use Get-IntelligenceXConfig to verify the effective result and layer origin.

## EXAMPLES

### EXAMPLE 1
```powershell
Set-IntelligenceXConfigValue -Key 'Value' -Value 'Value'
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

### -Key
Configuration key to write.

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

### -Value
Configuration value. Converted to JSON (string, number, boolean, array, object).

```yaml
Type: Object
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
