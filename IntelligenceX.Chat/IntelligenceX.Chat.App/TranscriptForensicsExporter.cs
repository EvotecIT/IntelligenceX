using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using IntelligenceX.Chat.App.Markdown;
using IntelligenceX.Chat.App.Rendering;
using OfficeIMO.MarkdownRenderer;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Builds forensic transcript snapshots so rendering issues can be diagnosed from raw persisted text
/// instead of relying on post-hoc UI heuristics alone.
/// </summary>
internal static class TranscriptForensicsExporter {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static void Export(string outputPath, TranscriptForensicsBundle bundle) {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(bundle);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(bundle, JsonOptions);
        File.WriteAllText(outputPath, json);
    }

    public static TranscriptForensicsBundle Build(
        string profileName,
        string? databasePath,
        string timestampFormat,
        MarkdownRendererOptions markdownOptions,
        string conversationId,
        string conversationTitle,
        string? threadId,
        IReadOnlyList<(string Role, string Text, DateTime Time, string? Model)> liveMessages,
        IReadOnlyList<ChatMessageState>? persistedMessages,
        RuntimeToolingSupportSnapshot? tooling = null,
        TranscriptForensicsTurnDiagnosticsSnapshot? turnDiagnostics = null) {
        ArgumentNullException.ThrowIfNull(markdownOptions);
        ArgumentNullException.ThrowIfNull(liveMessages);

        var liveSnapshot = BuildSnapshot(liveMessages, timestampFormat, markdownOptions);
        var persistedSnapshot = persistedMessages is null
            ? null
            : BuildPersistedSnapshot(persistedMessages, timestampFormat, markdownOptions);

        return new TranscriptForensicsBundle {
            ExportedUtc = DateTime.UtcNow,
            ProfileName = (profileName ?? string.Empty).Trim(),
            DatabasePath = NormalizeOptionalValue(databasePath),
            ConversationId = (conversationId ?? string.Empty).Trim(),
            ConversationTitle = string.IsNullOrWhiteSpace(conversationTitle) ? "New Chat" : conversationTitle.Trim(),
            ThreadId = NormalizeOptionalValue(threadId),
            TimestampFormat = string.IsNullOrWhiteSpace(timestampFormat) ? "HH:mm:ss" : timestampFormat.Trim(),
            Renderer = BuildRendererSnapshot(),
            Tooling = tooling,
            TurnDiagnostics = turnDiagnostics,
            Live = liveSnapshot,
            Persisted = persistedSnapshot
        };
    }

    private static TranscriptForensicsConversationSnapshot BuildSnapshot(
        IReadOnlyList<(string Role, string Text, DateTime Time, string? Model)> messages,
        string timestampFormat,
        MarkdownRendererOptions markdownOptions) {
        var sourceMessages = new List<TranscriptForensicsSourceMessage>(messages.Count);
        foreach (var message in messages) {
            sourceMessages.Add(new TranscriptForensicsSourceMessage {
                Role = message.Role,
                RawText = message.Text,
                DisplayTime = message.Time,
                TimeUtc = message.Time.Kind == DateTimeKind.Utc ? message.Time : message.Time.ToUniversalTime(),
                Model = message.Model
            });
        }

        return BuildConversationSnapshot(sourceMessages, timestampFormat, markdownOptions);
    }

    private static TranscriptForensicsConversationSnapshot BuildPersistedSnapshot(
        IReadOnlyList<ChatMessageState> persistedMessages,
        string timestampFormat,
        MarkdownRendererOptions markdownOptions) {
        var sourceMessages = new List<TranscriptForensicsSourceMessage>(persistedMessages.Count);

        for (var i = 0; i < persistedMessages.Count; i++) {
            var message = persistedMessages[i];
            var timeUtc = NormalizePersistedTimestampUtc(message.TimeUtc);
            sourceMessages.Add(new TranscriptForensicsSourceMessage {
                Role = message.Role,
                RawText = message.Text,
                DisplayTime = timeUtc.ToLocalTime(),
                TimeUtc = timeUtc,
                Model = message.Model
            });
        }

        return BuildConversationSnapshot(sourceMessages, timestampFormat, markdownOptions);
    }

    private static TranscriptForensicsConversationSnapshot BuildConversationSnapshot(
        IReadOnlyList<TranscriptForensicsSourceMessage> sourceMessages,
        string timestampFormat,
        MarkdownRendererOptions markdownOptions) {
        var transcriptMessages = new List<(string Role, string Text, DateTime Time, string? Model)>(sourceMessages.Count);
        var projectedMessages = new List<TranscriptForensicsMessage>(sourceMessages.Count);

        for (var i = 0; i < sourceMessages.Count; i++) {
            var message = sourceMessages[i];
            var rawText = message.RawText ?? string.Empty;
            var normalizedText = TranscriptMarkdownPreparation.PrepareMessageBody(message.Role, rawText);
            if (string.IsNullOrWhiteSpace(normalizedText)) {
                continue;
            }

            transcriptMessages.Add((message.Role, rawText, message.DisplayTime, message.Model));

            // Export snapshots keep per-message HTML, but we only need the message body here rather than
            // rebuilding full transcript-shell chrome for each entry.
            var renderedHtml = TranscriptHtmlFormatter.FormatSingleMessageForExport(
                message.Role,
                rawText,
                markdownOptions);

            projectedMessages.Add(new TranscriptForensicsMessage {
                Role = message.Role,
                TimeUtc = message.TimeUtc,
                Model = NormalizeOptionalValue(message.Model),
                RawText = rawText,
                NormalizedText = normalizedText,
                RenderedHtml = renderedHtml,
                WasNormalized = !string.Equals(rawText, normalizedText, StringComparison.Ordinal)
            });
        }

        var rawTranscriptMarkdown = TranscriptMarkdownDocumentBuilder.BuildRawTranscript(transcriptMessages, timestampFormat);
        var normalizedTranscriptMarkdown = TranscriptMarkdownDocumentBuilder.BuildPreparedTranscript(transcriptMessages, timestampFormat);
        var renderedTranscriptHtml = TranscriptHtmlFormatter.Format(transcriptMessages, timestampFormat, markdownOptions);

        return new TranscriptForensicsConversationSnapshot {
            MessageCount = projectedMessages.Count,
            RawTranscriptMarkdown = rawTranscriptMarkdown,
            NormalizedTranscriptMarkdown = normalizedTranscriptMarkdown,
            RenderedTranscriptHtml = renderedTranscriptHtml,
            Messages = projectedMessages
        };
    }

    private static DateTime NormalizePersistedTimestampUtc(DateTime timestampUtc) {
        return timestampUtc.Kind switch {
            DateTimeKind.Utc => timestampUtc,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc),
            _ => timestampUtc.ToUniversalTime()
        };
    }

    private static TranscriptForensicsRendererSnapshot BuildRendererSnapshot() {
        return new TranscriptForensicsRendererSnapshot {
            MarkdownRendererAssembly = OfficeImoAssemblyContractDiagnostics.DescribeMarkdownRendererContract(),
            MarkdownAssembly = OfficeImoAssemblyContractDiagnostics.DescribeMarkdownContract(),
            WordMarkdownAssembly = OfficeImoAssemblyContractDiagnostics.DescribeWordMarkdownContract()
        };
    }

    private static string? NormalizeOptionalValue(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}

internal sealed class TranscriptForensicsSourceMessage {
    public string Role { get; set; } = string.Empty;
    public string RawText { get; set; } = string.Empty;
    public DateTime DisplayTime { get; set; }
    public DateTime TimeUtc { get; set; }
    public string? Model { get; set; }
}

internal sealed class TranscriptForensicsBundle {
    public DateTime ExportedUtc { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public string? DatabasePath { get; set; }
    public string ConversationId { get; set; } = string.Empty;
    public string ConversationTitle { get; set; } = string.Empty;
    public string? ThreadId { get; set; }
    public string TimestampFormat { get; set; } = "HH:mm:ss";
    public TranscriptForensicsRendererSnapshot Renderer { get; set; } = new();
    public RuntimeToolingSupportSnapshot? Tooling { get; set; }
    public TranscriptForensicsTurnDiagnosticsSnapshot? TurnDiagnostics { get; set; }
    public TranscriptForensicsConversationSnapshot Live { get; set; } = new();
    public TranscriptForensicsConversationSnapshot? Persisted { get; set; }
}

internal sealed class TranscriptForensicsTurnDiagnosticsSnapshot {
    public List<string> ActivityTimeline { get; set; } = new();
    public List<TranscriptForensicsRoutingPromptExposureSnapshot> RoutingPromptExposureHistory { get; set; } = new();
    public TranscriptForensicsTurnMetricsSnapshot? LastTurnMetrics { get; set; }
}

internal sealed class TranscriptForensicsRoutingPromptExposureSnapshot {
    public string? RequestId { get; set; }
    public string? ThreadId { get; set; }
    public string Strategy { get; set; } = string.Empty;
    public int SelectedToolCount { get; set; }
    public int TotalToolCount { get; set; }
    public bool Reordered { get; set; }
    public List<string> TopToolNames { get; set; } = new();
}

internal sealed class TranscriptForensicsTurnMetricsSnapshot {
    public string RequestId { get; set; } = string.Empty;
    public DateTime CompletedUtc { get; set; }
    public long DurationMs { get; set; }
    public long? TtftMs { get; set; }
    public long? QueueWaitMs { get; set; }
    public long? AuthProbeMs { get; set; }
    public long? ConnectMs { get; set; }
    public long? EnsureThreadMs { get; set; }
    public long? WeightedSubsetSelectionMs { get; set; }
    public long? ResolveModelMs { get; set; }
    public long? DispatchToFirstStatusMs { get; set; }
    public long? DispatchToModelSelectedMs { get; set; }
    public long? DispatchToFirstToolRunningMs { get; set; }
    public long? DispatchToFirstDeltaMs { get; set; }
    public long? DispatchToLastDeltaMs { get; set; }
    public long? StreamDurationMs { get; set; }
    public int ToolCallsCount { get; set; }
    public int ToolRounds { get; set; }
    public int ProjectionFallbackCount { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public long? PromptTokens { get; set; }
    public long? CompletionTokens { get; set; }
    public long? TotalTokens { get; set; }
    public long? CachedPromptTokens { get; set; }
    public long? ReasoningTokens { get; set; }
    public string? Model { get; set; }
    public string? RequestedModel { get; set; }
    public string? Transport { get; set; }
    public string? EndpointHost { get; set; }
    public List<TranscriptForensicsAutonomyCounterSnapshot> AutonomyCounters { get; set; } = new();
}

internal sealed class TranscriptForensicsAutonomyCounterSnapshot {
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

internal sealed class TranscriptForensicsRendererSnapshot {
    public string MarkdownRendererAssembly { get; set; } = string.Empty;
    public string MarkdownAssembly { get; set; } = string.Empty;
    public string WordMarkdownAssembly { get; set; } = string.Empty;
}

internal sealed class TranscriptForensicsConversationSnapshot {
    public int MessageCount { get; set; }
    public string RawTranscriptMarkdown { get; set; } = string.Empty;
    public string NormalizedTranscriptMarkdown { get; set; } = string.Empty;
    public string RenderedTranscriptHtml { get; set; } = string.Empty;
    public List<TranscriptForensicsMessage> Messages { get; set; } = new();
}

internal sealed class TranscriptForensicsMessage {
    public string Role { get; set; } = string.Empty;
    public DateTime TimeUtc { get; set; }
    public string? Model { get; set; }
    public string RawText { get; set; } = string.Empty;
    public string NormalizedText { get; set; } = string.Empty;
    public string RenderedHtml { get; set; } = string.Empty;
    public bool WasNormalized { get; set; }
}
