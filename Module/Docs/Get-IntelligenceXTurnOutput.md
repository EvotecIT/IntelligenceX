---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Get-IntelligenceXTurnOutput
## SYNOPSIS
Extracts outputs from a turn.

Filters and saves outputs from a completed turn, including images and text.

## SYNTAX
### __AllParameterSets
```powershell
Get-IntelligenceXTurnOutput -Turn <TurnInfo> [-Images] [-Text] [-First <int>] [-SaveImagesTo <string>] [-DownloadUrls] [-Overwrite] [-FileNamePrefix <string>] [-PassThru] [<CommonParameters>]
```

## DESCRIPTION
Extracts outputs from a turn.

Filters and saves outputs from a completed turn, including images and text.

## EXAMPLES

### EXAMPLE 1
```powershell
Get-IntelligenceXTurnOutput -Turn 'Value'
```


## PARAMETERS

### -DownloadUrls
Download image URLs when saving images.

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

### -FileNamePrefix
Prefix for saved image file names.

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

### -First
Return only the first N outputs.

```yaml
Type: Int32
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Images
Return only image outputs.

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

### -Overwrite
Overwrite existing files when saving images.

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

### -PassThru
Output original TurnOutput objects when saving images.

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

### -SaveImagesTo
Save image outputs to the specified directory.

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

### -Text
Return only text outputs.

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

### -Turn
Turn to read outputs from.

```yaml
Type: TurnInfo
Parameter Sets: __AllParameterSets
Aliases: None
Possible values:

Required: True
Position: named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

- `IntelligenceX.OpenAI.AppServer.Models.TurnInfo`

## OUTPUTS

- `IntelligenceX.OpenAI.AppServer.Models.TurnOutput`

## RELATED LINKS

- None
