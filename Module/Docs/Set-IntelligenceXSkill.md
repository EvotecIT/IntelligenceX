---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Set-IntelligenceXSkill
## SYNOPSIS
Enables or disables a skill entry in app-server skill configuration.

Updates the persisted skill config for a specific skill path so future tool runs include
or exclude that skill.

## SYNTAX
### __AllParameterSets
```powershell
Set-IntelligenceXSkill -Path <string> -Enabled <bool> [-Client <IntelligenceXClient>] [<CommonParameters>]
```

## DESCRIPTION
Enables or disables a skill entry in app-server skill configuration.

Updates the persisted skill config for a specific skill path so future tool runs include
or exclude that skill.

## EXAMPLES

### EXAMPLE 1
```powershell
Set-IntelligenceXSkill -Path 'C:\Path' -Enabled $true
```


## PARAMETERS

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

### -Enabled
Set to $true to enable, $false to disable.

```yaml
Type: Boolean
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Path
Path to the skill configuration entry.

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
