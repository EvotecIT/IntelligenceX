---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Install-IntelligenceXCopilotCli
## SYNOPSIS
Installs GitHub Copilot CLI using a selected install strategy.

Executes the platform-specific installer command and optionally returns command metadata.
Supports WhatIf/Confirm through ShouldProcess.

## SYNTAX
### __AllParameterSets
```powershell
Install-IntelligenceXCopilotCli [-Method <CopilotCliInstallMethod>] [-Prerelease] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Installs GitHub Copilot CLI using a selected install strategy.

Executes the platform-specific installer command and optionally returns command metadata.
Supports WhatIf/Confirm through ShouldProcess.

## EXAMPLES

### EXAMPLE 1
```powershell
Install-IntelligenceXCopilotCli -Method 'Value'
```


## PARAMETERS

### -Method
Install method to use (Auto, Winget, Brew, Apt, etc. depending on platform).

```yaml
Type: CopilotCliInstallMethod
Parameter Sets: __AllParameterSets
Aliases: None
Possible values: Auto, Winget, Homebrew, Npm, Script

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PassThru
Returns the resolved install command object after successful execution.

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

### -Prerelease
Installs a prerelease build when available.

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
