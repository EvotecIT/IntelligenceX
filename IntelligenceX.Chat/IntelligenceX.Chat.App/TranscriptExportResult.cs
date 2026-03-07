namespace IntelligenceX.Chat.App;

internal enum TranscriptExportOutcomeKind {
    Succeeded,
    SucceededWithFallback,
    Failed
}

internal enum TranscriptExportFallbackKind {
    None,
    Markdown,
    DocxWithoutMaterializedVisuals
}

internal enum TranscriptExportStage {
    None,
    MarkdownWrite,
    MarkdownFallbackWrite,
    DocxWrite,
    DocxWriteWithMaterializedVisuals,
    DocxWriteWithoutMaterializedVisuals
}

internal readonly record struct TranscriptExportFailure(TranscriptExportStage Stage, string Message);

internal readonly record struct TranscriptExportFallback(
    TranscriptExportFallbackKind Kind,
    string OutputPath,
    TranscriptExportFailure Cause);

internal readonly record struct TranscriptExportResult(
    TranscriptExportOutcomeKind OutcomeKind,
    string RequestedFormat,
    string ActualFormat,
    string OutputPath,
    TranscriptExportFailure? Failure = null,
    TranscriptExportFallback? Fallback = null) {
    public bool Succeeded => OutcomeKind != TranscriptExportOutcomeKind.Failed;
    public bool UsedFallback => OutcomeKind == TranscriptExportOutcomeKind.SucceededWithFallback;

    public static TranscriptExportResult Success(string requestedFormat, string actualFormat, string outputPath) {
        return new(
            TranscriptExportOutcomeKind.Succeeded,
            NormalizeFormat(requestedFormat),
            NormalizeFormat(actualFormat),
            outputPath ?? string.Empty);
    }

    public static TranscriptExportResult SuccessWithFallback(
        string requestedFormat,
        string actualFormat,
        string outputPath,
        TranscriptExportFallback fallback) {
        return new(
            TranscriptExportOutcomeKind.SucceededWithFallback,
            NormalizeFormat(requestedFormat),
            NormalizeFormat(actualFormat),
            outputPath ?? string.Empty,
            Failure: null,
            Fallback: fallback);
    }

    public static TranscriptExportResult Failed(
        string requestedFormat,
        string outputPath,
        TranscriptExportFailure failure,
        TranscriptExportFallback? fallback = null) {
        return new(
            TranscriptExportOutcomeKind.Failed,
            NormalizeFormat(requestedFormat),
            string.Empty,
            outputPath ?? string.Empty,
            failure,
            fallback);
    }

    private static string NormalizeFormat(string? format) {
        return (format ?? string.Empty).Trim().ToLowerInvariant();
    }
}
