---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Send-IntelligenceXFeedback
## SYNOPSIS
Uploads textual feedback to the app-server feedback endpoint.

Use this cmdlet to send reproduction notes, quality feedback, or analyzer observations
collected during reviews and local runs.

## SYNTAX
### __AllParameterSets
```powershell
Send-IntelligenceXFeedback -Content <string> [-Client <IntelligenceXClient>] [<CommonParameters>]
```

## DESCRIPTION
Uploads textual feedback to the app-server feedback endpoint.

Use this cmdlet to send reproduction notes, quality feedback, or analyzer observations
collected during reviews and local runs.

## EXAMPLES

### EXAMPLE 1
```powershell
Send-IntelligenceXFeedback -Content 'Value'
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

### -Content
Feedback text content to upload.

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
