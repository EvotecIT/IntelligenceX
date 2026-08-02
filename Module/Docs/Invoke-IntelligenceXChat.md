---
external help file: IntelligenceX-help.xml
Module Name: IntelligenceX
online version: https://github.com/EvotecIT/IntelligenceX/blob/master/README.md
schema: 2.0.0
---
# Invoke-IntelligenceXChat
## SYNOPSIS
Super-easy chat command that handles connect, init, login, thread, and send.

Convenience entry point for quick chat: it connects, logs in (if needed), creates or
reuses a thread, sends input, and returns the resulting turn. Supports streaming output and
a simple DSL for text/image inputs.

## SYNTAX
### Text (Default)
```powershell
Invoke-IntelligenceXChat [-Text] <string> [-Dsl] [-Model <string>] [-Login <string>] [-ApiKey <string>] [-OpenBrowser] [-Stream] [-WaitSeconds <int>] [-NewThread] [-ClientName <string>] [-ClientTitle <string>] [-ClientVersion <string>] [-ExecutablePath <string>] [-Arguments <string>] [-WorkingDirectory <string>] [-Workspace <string>] [-AllowNetwork] [-ApprovalPolicy <string>] [-ImagePath <string>] [-ImageUrl <string>] [-Instructions <string>] [-ReasoningEffort <ReasoningEffort>] [-ReasoningSummary <ReasoningSummary>] [-TextVerbosity <TextVerbosity>] [-Temperature <double>] [-SaveImagesTo <string>] [-DownloadImageUrls] [-OverwriteImages] [-ImageFileNamePrefix <string>] [-Raw] [<CommonParameters>]
```

### Pipeline
```powershell
Invoke-IntelligenceXChat [-InputObject <string>] [-Dsl] [-Model <string>] [-Login <string>] [-ApiKey <string>] [-OpenBrowser] [-Stream] [-WaitSeconds <int>] [-NewThread] [-ClientName <string>] [-ClientTitle <string>] [-ClientVersion <string>] [-ExecutablePath <string>] [-Arguments <string>] [-WorkingDirectory <string>] [-Workspace <string>] [-AllowNetwork] [-ApprovalPolicy <string>] [-ImagePath <string>] [-ImageUrl <string>] [-Instructions <string>] [-ReasoningEffort <ReasoningEffort>] [-ReasoningSummary <ReasoningSummary>] [-TextVerbosity <TextVerbosity>] [-Temperature <double>] [-SaveImagesTo <string>] [-DownloadImageUrls] [-OverwriteImages] [-ImageFileNamePrefix <string>] [-Raw] [<CommonParameters>]
```

## DESCRIPTION
Super-easy chat command that handles connect, init, login, thread, and send.

Convenience entry point for quick chat: it connects, logs in (if needed), creates or
reuses a thread, sends input, and returns the resulting turn. Supports streaming output and
a simple DSL for text/image inputs.

## EXAMPLES

### EXAMPLE 1
```powershell
Invoke-IntelligenceXChat -ExecutablePath 'C:\Path'
```


## PARAMETERS

### -AllowNetwork
Allow network access when using a workspace sandbox.

```yaml
Type: SwitchParameter
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ApiKey
API key to use when Login is ApiKey.

```yaml
Type: String
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ApprovalPolicy
Approval policy (for example auto).

```yaml
Type: String
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Arguments
Codex app-server arguments.

```yaml
Type: String
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ClientName
Client name for initialization.

```yaml
Type: String
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ClientTitle
Client title for initialization.

```yaml
Type: String
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ClientVersion
Client version for initialization.

```yaml
Type: String
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -DownloadImageUrls
Download image URLs when saving images.

```yaml
Type: SwitchParameter
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Dsl
Parse Text as a simple DSL (lines starting with image:, url:, text:).

```yaml
Type: SwitchParameter
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ExecutablePath
Codex executable path.

```yaml
Type: String
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ImageFileNamePrefix
Prefix for saved image file names.

```yaml
Type: String
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ImagePath
Image file path to include with the message.

```yaml
Type: String
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ImageUrl
Image URL to include with the message.

```yaml
Type: String
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -InputObject
Pipeline input for DSL lines (text, image, url).

```yaml
Type: String
Parameter Sets: Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -Instructions
System instructions for the assistant.

```yaml
Type: String
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Login
Login method: ChatGpt, ApiKey, or None.

```yaml
Type: String
Parameter Sets: Text, Pipeline
Aliases: None
Possible values: ChatGpt, ApiKey, None

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Model
Model identifier. Defaults to the shared OpenAI default model.

```yaml
Type: String
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -NewThread
Reset thread before sending.

```yaml
Type: SwitchParameter
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OpenBrowser
Open the login URL in the default browser.

```yaml
Type: SwitchParameter
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OverwriteImages
Overwrite existing files when saving images.

```yaml
Type: SwitchParameter
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Raw
Return raw JSON response.

```yaml
Type: SwitchParameter
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ReasoningEffort
Reasoning effort level.

```yaml
Type: Nullable`1
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ReasoningSummary
Reasoning summary level.

```yaml
Type: Nullable`1
Parameter Sets: Text, Pipeline
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
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Stream
Write streaming deltas to the host.

```yaml
Type: SwitchParameter
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Temperature
Sampling temperature.

```yaml
Type: Nullable`1
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Text
Message text to send.

```yaml
Type: String
Parameter Sets: Text
Aliases: None
Possible values:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -TextVerbosity
Response verbosity level.

```yaml
Type: Nullable`1
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WaitSeconds
Wait N seconds for streaming output after sending.

```yaml
Type: Int32
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WorkingDirectory
Working directory for app-server.

```yaml
Type: String
Parameter Sets: Text, Pipeline
Aliases: None
Possible values:

Required: False
Position: named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Workspace
Workspace directory for file writes (sets sandbox policy).

```yaml
Type: String
Parameter Sets: Text, Pipeline
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

- `System.String`

## OUTPUTS

- `IntelligenceX.OpenAI.AppServer.Models.TurnInfo`
- `IntelligenceX.Json.JsonValue`

## RELATED LINKS

- None
