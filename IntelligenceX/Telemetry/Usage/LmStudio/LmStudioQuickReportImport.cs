using System;
using System.Collections.Generic;
using System.Threading;

namespace IntelligenceX.Telemetry.Usage.LmStudio;

internal static class LmStudioQuickReportImport {
    public static IEnumerable<string> EnumerateCandidateFiles(string rootPath, bool preferRecentArtifacts) {
        foreach (var file in LmStudioConversationImportSupport.EnumerateCandidateFiles(rootPath, preferRecentArtifacts)) {
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
        var records = LmStudioConversationImport.ParseFile(
            filePath,
            root,
            adapterId,
            options.MachineId,
            cancellationToken);
        var aggregates = new Dictionary<string, UsageTelemetryQuickReportSupport.UsageAggregateBucket>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records) {
            cancellationToken.ThrowIfCancellationRequested();
            UsageTelemetryQuickReportSupport.AddAggregate(
                aggregates,
                providerId: providerId,
                sourceRootId: root.Id,
                dayUtc: record.TimestampUtc.UtcDateTime.Date,
                model: record.Model ?? "unknown-model",
                surface: record.Surface ?? "chat",
                machineId: record.MachineId,
                accountLabel: record.AccountLabel,
                inputTokens: record.InputTokens ?? 0L,
                cachedInputTokens: record.CachedInputTokens ?? 0L,
                outputTokens: record.OutputTokens ?? 0L,
                reasoningTokens: record.ReasoningTokens ?? 0L,
                totalTokens: record.TotalTokens ?? 0L);
        }

        return UsageTelemetryQuickReportSupport.FinalizeAggregates(aggregates, adapterId);
    }
}
