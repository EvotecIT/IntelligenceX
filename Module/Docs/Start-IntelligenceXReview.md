---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Start-IntelligenceXReview
## SYNOPSIS
Starts a review flow for a thread.

Triggers the app-server review pipeline for a thread and target. Use TargetType to
choose what to review (uncommitted changes, a branch, a commit, or custom text).

## SYNTAX
### __AllParameterSets
```powershell
Start-IntelligenceXReview -ThreadId <string> -Delivery <string> -TargetType <string> [-Client <IntelligenceXClient>] [-TargetValue <string>] [-Raw] [<CommonParameters>]
```

## DESCRIPTION
Starts a review flow for a thread.

Triggers the app-server review pipeline for a thread and target. Use TargetType to
choose what to review (uncommitted changes, a branch, a commit, or custom text).

## EXAMPLES

### EXAMPLE 1
```powershell
Start-IntelligenceXReview -ThreadId 'Value' -Delivery 'Value' -TargetType 'Value'
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

### -Delivery
Delivery mode (for example immediate).

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

### -Raw
Return raw JSON response.

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

### -TargetType
Target type: uncommittedChanges, baseBranch, commit, custom.

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

### -TargetValue
Target value (branch name, commit SHA, or custom text).

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

### -ThreadId
Thread identifier.

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

- `IntelligenceX.OpenAI.AppServer.Models.ReviewStartResult`
- `IntelligenceX.Json.JsonValue`

## RELATED LINKS

- None
