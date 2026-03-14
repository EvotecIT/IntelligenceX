using System;
using System.Collections.Generic;
using System.Threading;
using IntelligenceX.Json;

namespace IntelligenceX.Telemetry.Usage.Codex;

internal static class CodexQuickReportImport {
    public static IEnumerable<string> EnumerateCandidateFiles(string rootPath, bool preferRecentArtifacts) {
        foreach (var file in CodexSessionImportSupport.EnumerateCandidateFiles(rootPath, preferRecentArtifacts)) {
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
        var aggregates = new Dictionary<string, UsageTelemetryQuickReportSupport.UsageAggregateBucket>(StringComparer.OrdinalIgnoreCase);
        var currentModel = default(string);
        CodexSessionImportSupport.CodexNormalizedUsage? previousTotals = null;
        CodexSessionImportSupport.CodexNormalizedUsage? previousLastUsage = null;

        foreach (var rawLine in UsageTelemetryQuickReportSupport.ReadLinesShared(filePath)) {
            cancellationToken.ThrowIfCancellationRequested();
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

            var type = entry.GetString("type");
            var payload = entry.GetObject("payload");
            if (string.Equals(type, "session_meta", StringComparison.OrdinalIgnoreCase)) {
                currentModel = CodexSessionImportSupport.ExtractModel(payload) ?? currentModel;
                continue;
            }
            if (string.Equals(type, "turn_context", StringComparison.OrdinalIgnoreCase)) {
                currentModel = CodexSessionImportSupport.ExtractModel(payload) ?? currentModel;
                continue;
            }
            if (!string.Equals(type, "event_msg", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(payload?.GetString("type"), "token_count", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var info = payload?.GetObject("info");
            var lastUsage = CodexSessionImportSupport.NormalizeUsage(info?.GetObject("last_token_usage") ?? info?.GetObject("lastTokenUsage"));
            var totalUsage = CodexSessionImportSupport.NormalizeUsage(info?.GetObject("total_token_usage") ?? info?.GetObject("totalTokenUsage"));
            if (totalUsage is not null && previousTotals is not null && totalUsage == previousTotals) {
                if (lastUsage is not null) {
                    previousLastUsage = lastUsage;
                }
                continue;
            }

            CodexSessionImportSupport.CodexNormalizedUsage? usage = null;
            if (lastUsage is not null) {
                if (previousLastUsage is not null && lastUsage == previousLastUsage) {
                    if (totalUsage is not null) {
                        previousTotals = totalUsage;
                    }
                    continue;
                }

                usage = lastUsage;
                previousLastUsage = lastUsage;
            } else if (totalUsage is not null) {
                usage = CodexSessionImportSupport.SubtractUsage(totalUsage, previousTotals);
            }

            if (totalUsage is not null) {
                previousTotals = totalUsage;
            }

            if (usage is null || usage.TotalTokens <= 0) {
                continue;
            }
            if (!UsageTelemetryQuickReportSupport.TryParseTimestampUtc(entry.GetString("timestamp"), out var timestampUtc)) {
                continue;
            }

            UsageTelemetryQuickReportSupport.AddAggregate(
                aggregates,
                providerId: providerId,
                sourceRootId: root.Id,
                dayUtc: timestampUtc.UtcDateTime.Date,
                model: CodexSessionImportSupport.ExtractModel(payload) ?? currentModel ?? "unknown-model",
                surface: "cli",
                machineId: UsageTelemetryQuickReportSupport.NormalizeOptional(options.MachineId) ?? UsageTelemetryQuickReportSupport.NormalizeOptional(root.MachineLabel),
                accountLabel: UsageTelemetryQuickReportSupport.NormalizeOptional(root.AccountHint),
                inputTokens: usage.InputTokens,
                cachedInputTokens: usage.CachedInputTokens,
                outputTokens: usage.OutputTokens,
                reasoningTokens: usage.ReasoningTokens,
                totalTokens: usage.TotalTokens);
        }

        return UsageTelemetryQuickReportSupport.FinalizeAggregates(aggregates, adapterId);
    }
}
