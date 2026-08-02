---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Backup-IntelligenceXThread
## SYNOPSIS
Archives an existing thread so it no longer appears in active workflows.

Use this cmdlet to hide completed or obsolete conversations from normal thread listings while
preserving history. The archive operation is non-destructive and can be used for housekeeping.

## SYNTAX
### __AllParameterSets
```powershell
Backup-IntelligenceXThread -ThreadId <string> [-Client <IntelligenceXClient>] [<CommonParameters>]
```

## DESCRIPTION
Archives an existing thread so it no longer appears in active workflows.

Use this cmdlet to hide completed or obsolete conversations from normal thread listings while
preserving history. The archive operation is non-destructive and can be used for housekeeping.

## EXAMPLES

### EXAMPLE 1
```powershell
Backup-IntelligenceXThread -ThreadId 'Value'
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

### -ThreadId
Identifier of the thread to archive.

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
