using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
        IReadOnlyList<ChatMessageState>? persistedMessages) {
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
            Live = liveSnapshot,
            Persisted = persistedSnapshot
        };
    }

    private static TranscriptForensicsConversationSnapshot BuildSnapshot(
        IReadOnlyList<(string Role, string Text, DateTime Time, string? Model)> messages,
        string timestampFormat,
        MarkdownRendererOptions markdownOptions) {
        var includedMessages = new List<(string Role, string Text, DateTime Time, string? Model)>(messages.Count);
        var projectedMessages = new List<TranscriptForensicsMessage>(messages.Count);
        foreach (var message in messages) {
            var rawText = message.Text;
            var normalizedText = TranscriptMarkdownNormalizer.NormalizeForRendering(rawText);
            if (string.IsNullOrWhiteSpace(normalizedText)) {
                continue;
            }

            includedMessages.Add((message.Role, rawText, message.Time, message.Model));
            // Export snapshots keep per-message HTML, but we only need the message body here rather than
            // rebuilding full transcript-shell chrome for each entry.
            var renderedHtml = TranscriptHtmlFormatter.FormatSingleMessageForExport(
                message.Role,
                rawText,
                markdownOptions);

            projectedMessages.Add(new TranscriptForensicsMessage {
                Role = message.Role,
                TimeUtc = message.Time.Kind == DateTimeKind.Utc ? message.Time : message.Time.ToUniversalTime(),
                Model = NormalizeOptionalValue(message.Model),
                RawText = rawText,
                NormalizedText = normalizedText,
                RenderedHtml = renderedHtml,
                WasNormalized = !string.Equals(rawText, normalizedText, StringComparison.Ordinal)
            });
        }

        var rawTranscriptMarkdown = BuildRawTranscriptMarkdown(includedMessages, timestampFormat);
        var normalizedTranscriptMarkdown = LocalExportArtifactWriter.NormalizeTranscriptMarkdownForExport(rawTranscriptMarkdown);
        var renderedTranscriptHtml = TranscriptHtmlFormatter.Format(includedMessages, timestampFormat, markdownOptions);

        return new TranscriptForensicsConversationSnapshot {
            MessageCount = projectedMessages.Count,
            RawTranscriptMarkdown = rawTranscriptMarkdown,
            NormalizedTranscriptMarkdown = normalizedTranscriptMarkdown,
            RenderedTranscriptHtml = renderedTranscriptHtml,
            Messages = projectedMessages
        };
    }

    private static TranscriptForensicsConversationSnapshot BuildPersistedSnapshot(
        IReadOnlyList<ChatMessageState> persistedMessages,
        string timestampFormat,
        MarkdownRendererOptions markdownOptions) {
        var displayMessages = new List<(string Role, string Text, DateTime Time, string? Model)>(persistedMessages.Count);
        var projectedMessages = new List<TranscriptForensicsMessage>(persistedMessages.Count);

        for (var i = 0; i < persistedMessages.Count; i++) {
            var message = persistedMessages[i];
            var timeUtc = NormalizePersistedTimestampUtc(message.TimeUtc);
            var displayTime = timeUtc.ToLocalTime();
            var rawText = message.Text;
            var normalizedText = TranscriptMarkdownNormalizer.NormalizeForRendering(rawText);
            if (string.IsNullOrWhiteSpace(normalizedText)) {
                continue;
            }

            displayMessages.Add((message.Role, rawText, displayTime, message.Model));
            var renderedHtml = TranscriptHtmlFormatter.FormatSingleMessageForExport(
                message.Role,
                rawText,
                markdownOptions);

            projectedMessages.Add(new TranscriptForensicsMessage {
                Role = message.Role,
                TimeUtc = timeUtc,
                Model = NormalizeOptionalValue(message.Model),
                RawText = rawText,
                NormalizedText = normalizedText,
                RenderedHtml = renderedHtml,
                WasNormalized = !string.Equals(rawText, normalizedText, StringComparison.Ordinal)
            });
        }

        var rawTranscriptMarkdown = BuildRawTranscriptMarkdown(displayMessages, timestampFormat);
        var normalizedTranscriptMarkdown = LocalExportArtifactWriter.NormalizeTranscriptMarkdownForExport(rawTranscriptMarkdown);
        var renderedTranscriptHtml = TranscriptHtmlFormatter.Format(displayMessages, timestampFormat, markdownOptions);

        return new TranscriptForensicsConversationSnapshot {
            MessageCount = projectedMessages.Count,
            RawTranscriptMarkdown = rawTranscriptMarkdown,
            NormalizedTranscriptMarkdown = normalizedTranscriptMarkdown,
            RenderedTranscriptHtml = renderedTranscriptHtml,
            Messages = projectedMessages
        };
    }

    private static string BuildRawTranscriptMarkdown(
        IReadOnlyList<(string Role, string Text, DateTime Time, string? Model)> messages,
        string timestampFormat) {
        var markdown = new MarkdownComposer();
        var format = string.IsNullOrWhiteSpace(timestampFormat) ? "HH:mm:ss" : timestampFormat.Trim();

        foreach (var message in messages) {
            if (string.IsNullOrWhiteSpace(message.Text)) {
                continue;
            }

            markdown.Heading($"{message.Role} ({message.Time.ToString(format)})", 3);
            var modelComment = BuildModelComment(message.Role, message.Model);
            if (!string.IsNullOrWhiteSpace(modelComment)) {
                markdown.Raw(modelComment);
            }

            markdown.Raw(message.Text).BlankLine();
        }

        return markdown.Build();
    }

    private static string BuildModelComment(string role, string? model) {
        var normalizedRole = (role ?? string.Empty).Trim();
        if (!normalizedRole.Equals("Assistant", StringComparison.OrdinalIgnoreCase)
            && !normalizedRole.Equals("Tools", StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }

        var normalizedModel = (model ?? string.Empty).Trim();
        if (normalizedModel.Length == 0) {
            return string.Empty;
        }

        var safeModel = normalizedModel
            .Replace("--", "- -", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
        return "<!-- ix:model: " + safeModel + " -->";
    }

    private static DateTime NormalizePersistedTimestampUtc(DateTime timestampUtc) {
        return timestampUtc.Kind switch {
            DateTimeKind.Utc => timestampUtc,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc),
            _ => timestampUtc.ToUniversalTime()
        };
    }

    private static TranscriptForensicsRendererSnapshot BuildRendererSnapshot() {
        var rendererAssembly = typeof(MarkdownRenderer).Assembly;
        var markdownAssembly = Type.GetType(
            "OfficeIMO.Markdown.MarkdownInputNormalizer, OfficeIMO.Markdown",
            throwOnError: false)?.Assembly;

        return new TranscriptForensicsRendererSnapshot {
            MarkdownRendererAssembly = BuildAssemblyIdentity(rendererAssembly),
            MarkdownAssembly = markdownAssembly is null ? "unavailable" : BuildAssemblyIdentity(markdownAssembly)
        };
    }

    private static string BuildAssemblyIdentity(Assembly assembly) {
        var name = assembly.GetName();
        var version = name.Version?.ToString() ?? "unknown";
        var location = string.Empty;
        try {
            location = assembly.Location ?? string.Empty;
        } catch {
            location = string.Empty;
        }

        var path = string.IsNullOrWhiteSpace(location)
            ? "(dynamic)"
            : Path.GetFullPath(location);
        return $"{name.Name} version={version} path={path}";
    }

    private static string? NormalizeOptionalValue(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }
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
    public TranscriptForensicsConversationSnapshot Live { get; set; } = new();
    public TranscriptForensicsConversationSnapshot? Persisted { get; set; }
}

internal sealed class TranscriptForensicsRendererSnapshot {
    public string MarkdownRendererAssembly { get; set; } = string.Empty;
    public string MarkdownAssembly { get; set; } = string.Empty;
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
