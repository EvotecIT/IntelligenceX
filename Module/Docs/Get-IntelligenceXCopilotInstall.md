---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Get-IntelligenceXCopilotInstall
## SYNOPSIS
Shows platform-specific installation commands for GitHub Copilot CLI.

This cmdlet does not install anything. It returns suggested install command metadata
so you can preview, log, or execute it manually.

## SYNTAX
### __AllParameterSets
```powershell
Get-IntelligenceXCopilotInstall [-Prerelease] [<CommonParameters>]
```

## DESCRIPTION
Shows platform-specific installation commands for GitHub Copilot CLI.

This cmdlet does not install anything. It returns suggested install command metadata
so you can preview, log, or execute it manually.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-IntelligenceXCopilotInstall -Prerelease
```


## PARAMETERS

### -Prerelease
Returns commands for installing prerelease builds.

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

- `None`

## OUTPUTS

- `IntelligenceX.Copilot.CopilotCliInstallCommand`

## RELATED LINKS

- None
