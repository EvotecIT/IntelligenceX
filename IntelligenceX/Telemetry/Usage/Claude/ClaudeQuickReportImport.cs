using System;
using System.Collections.Generic;
using System.Threading;
using IntelligenceX.Json;

namespace IntelligenceX.Telemetry.Usage.Claude;

internal static class ClaudeQuickReportImport {
    public static IEnumerable<string> EnumerateCandidateFiles(string rootPath, bool preferRecentArtifacts) {
        foreach (var file in ClaudeSessionImportSupport.EnumerateCandidateFiles(rootPath, preferRecentArtifacts)) {
            yield return file;
        }
    }

    public static IReadOnlyList<UsageEventRecord> ParseFile(
        SourceRootRecord root,
        string filePath,
        UsageTelemetryQuickReportOptions options,
        string adapterId,
        string providerId,
        CancellationToken cancellationToken) {
        var candidates = new Dictionary<string, ClaudeUsageCandidate>(StringComparer.OrdinalIgnoreCase);
        var lineNumber = 0;
        foreach (var rawLine in UsageTelemetryQuickReportSupport.ReadLinesShared(filePath)) {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            if (string.IsNullOrWhiteSpace(rawLine)) {
                continue;
            }

            JsonObject entry;
            try {
                entry = JsonLite.Parse(rawLine).AsObject()
                        ?? throw new FormatException("Session line did not contain a JSON object.");
            } catch {
                continue;
            }

            if (!string.Equals(entry.GetString("type"), "assistant", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var message = entry.GetObject("message");
            var usage = ClaudeSessionImportSupport.NormalizeUsage(message?.GetObject("usage"));
            if (usage is null || usage.TotalTokens <= 0) {
                continue;
            }
            if (!UsageTelemetryQuickReportSupport.TryParseTimestampUtc(entry.GetString("timestamp"), out var timestampUtc)) {
                continue;
            }

            var key = ClaudeSessionImportSupport.BuildCandidateKey(
                ClaudeSessionImportSupport.ExtractMessageId(message),
                ClaudeSessionImportSupport.ExtractRequestId(entry),
                lineNumber);
            var candidate = new ClaudeUsageCandidate(
                key,
                timestampUtc,
                ClaudeSessionImportSupport.ExtractModel(message) ?? "unknown-model",
                usage,
                lineNumber);

            if (candidates.TryGetValue(key, out var existing) &&
                !ClaudeSessionImportSupport.ShouldReplaceExisting(
                    existing.Usage.TotalTokens,
                    existing.TimestampUtc,
                    existing.LineNumber,
                    candidate.Usage.TotalTokens,
                    candidate.TimestampUtc,
                    candidate.LineNumber)) {
                continue;
            }

            candidates[key] = candidate;
        }

        var aggregates = new Dictionary<string, UsageTelemetryQuickReportSupport.UsageAggregateBucket>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates.Values
                     .OrderBy(static value => value.TimestampUtc)
                     .ThenBy(static value => value.LineNumber)) {
            UsageTelemetryQuickReportSupport.AddAggregate(
                aggregates,
                providerId: providerId,
                sourceRootId: root.Id,
                dayUtc: candidate.TimestampUtc.UtcDateTime.Date,
                model: candidate.Model,
                surface: "cli",
                machineId: UsageTelemetryQuickReportSupport.NormalizeOptional(options.MachineId) ?? UsageTelemetryQuickReportSupport.NormalizeOptional(root.MachineLabel),
                accountLabel: UsageTelemetryQuickReportSupport.NormalizeOptional(root.AccountHint),
                inputTokens: candidate.Usage.InputTokens,
                cachedInputTokens: candidate.Usage.CachedInputTokens,
                outputTokens: candidate.Usage.OutputTokens,
                reasoningTokens: candidate.Usage.ReasoningTokens,
                totalTokens: candidate.Usage.TotalTokens);
        }

        return UsageTelemetryQuickReportSupport.FinalizeAggregates(aggregates, adapterId);
    }

    private sealed record ClaudeUsageCandidate(
        string Key,
        DateTimeOffset TimestampUtc,
        string Model,
        ClaudeSessionImportSupport.ClaudeNormalizedUsage Usage,
        int LineNumber);
}
