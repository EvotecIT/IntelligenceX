using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelligenceX.Json;

namespace IntelligenceX.Telemetry.Usage.Claude;

internal static class ClaudeSessionImportSupport {
    public static bool IsClaudeProvider(string? providerId) {
        var normalized = UsageTelemetryQuickReportSupport.NormalizeOptional(providerId);
        return string.Equals(normalized, "claude", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "anthropic-claude", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "claude-code", StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> EnumerateCandidateFiles(string rootPath, bool preferRecentArtifacts) {
        if (File.Exists(rootPath)) {
            if (string.Equals(Path.GetExtension(rootPath), ".jsonl", StringComparison.OrdinalIgnoreCase)) {
                yield return Path.GetFullPath(rootPath);
            }
            yield break;
        }

        foreach (var directory in EnumerateCandidateDirectories(rootPath)) {
            var files = UsageTelemetryQuickReportSupport.EnumerateFilesSafe(directory, "*.jsonl", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .ToArray();
            var orderedFiles = preferRecentArtifacts
                ? files.OrderByDescending(UsageTelemetryQuickReportSupport.GetLastWriteTimeUtcSafe)
                    .ThenBy(static value => value, StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase);

            foreach (var file in orderedFiles) {
                yield return file;
            }
        }
    }

    public static string? ExtractSessionId(JsonObject entry) {
        return UsageTelemetryQuickReportSupport.NormalizeOptional(
            entry.GetString("sessionId")
            ?? entry.GetString("session_id")
            ?? entry.GetString("conversationId")
            ?? entry.GetString("conversation_id"));
    }

    public static string? ExtractRequestId(JsonObject entry) {
        return UsageTelemetryQuickReportSupport.NormalizeOptional(entry.GetString("requestId") ?? entry.GetString("request_id"));
    }

    public static string? ExtractMessageId(JsonObject? message) {
        return UsageTelemetryQuickReportSupport.NormalizeOptional(message?.GetString("id"));
    }

    public static string? ExtractModel(JsonObject? message) {
        return UsageTelemetryQuickReportSupport.NormalizeOptional(message?.GetString("model"));
    }

    public static ClaudeNormalizedUsage? NormalizeUsage(JsonObject? usage) {
        if (usage is null) {
            return null;
        }

        var inputTokens = UsageTelemetryQuickReportSupport.ReadInt64(usage, "input_tokens", "inputTokens") ?? 0;
        var cacheCreationTokens =
            UsageTelemetryQuickReportSupport.ReadInt64(usage, "cache_creation_input_tokens", "cacheCreationInputTokens")
            ?? 0;
        var cacheReadTokens =
            UsageTelemetryQuickReportSupport.ReadInt64(usage, "cache_read_input_tokens", "cacheReadInputTokens")
            ?? 0;
        var outputTokens = UsageTelemetryQuickReportSupport.ReadInt64(usage, "output_tokens", "outputTokens") ?? 0;
        var reasoningTokens =
            UsageTelemetryQuickReportSupport.ReadInt64(usage, "reasoning_tokens", "reasoningTokens")
            ?? UsageTelemetryQuickReportSupport.ReadInt64(
                usage.GetObject("output_tokens_details"),
                "reasoning_tokens",
                "reasoningTokens")
            ?? 0;

        var totalTokens = UsageTelemetryQuickReportSupport.ReadInt64(usage, "total_tokens", "totalTokens")
                          ?? Math.Max(0, inputTokens + cacheCreationTokens + cacheReadTokens + outputTokens);

        return new ClaudeNormalizedUsage(
            InputTokens: Math.Max(0, inputTokens + cacheCreationTokens),
            CachedInputTokens: Math.Max(0, cacheReadTokens),
            OutputTokens: Math.Max(0, outputTokens),
            ReasoningTokens: Math.Max(0, reasoningTokens),
            TotalTokens: Math.Max(0, totalTokens));
    }

    public static string BuildCandidateKey(string? messageId, string? requestId, int lineNumber) {
        if (!string.IsNullOrWhiteSpace(messageId) || !string.IsNullOrWhiteSpace(requestId)) {
            return (messageId ?? string.Empty) + "|" + (requestId ?? string.Empty);
        }

        return "line|" + lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static bool ShouldReplaceExisting(
        long existingTotalTokens,
        DateTimeOffset existingTimestampUtc,
        int existingLineNumber,
        long candidateTotalTokens,
        DateTimeOffset candidateTimestampUtc,
        int candidateLineNumber) {
        if (candidateTotalTokens > existingTotalTokens) {
            return true;
        }

        if (candidateTotalTokens < existingTotalTokens) {
            return false;
        }

        if (candidateTimestampUtc > existingTimestampUtc) {
            return true;
        }

        return candidateTimestampUtc == existingTimestampUtc && candidateLineNumber > existingLineNumber;
    }

    private static IEnumerable<string> EnumerateCandidateDirectories(string rootPath) {
        if (!Directory.Exists(rootPath)) {
            yield break;
        }

        var normalizedRoot = Path.GetFullPath(rootPath);
        var rootName = Path.GetFileName(normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(rootName, "projects", StringComparison.OrdinalIgnoreCase)) {
            yield return normalizedRoot;
            yield break;
        }

        var projectsPath = Path.Combine(normalizedRoot, "projects");
        yield return Directory.Exists(projectsPath) ? projectsPath : normalizedRoot;
    }

    internal sealed record ClaudeNormalizedUsage(
        long InputTokens,
        long CachedInputTokens,
        long OutputTokens,
        long ReasoningTokens,
        long TotalTokens);
}
