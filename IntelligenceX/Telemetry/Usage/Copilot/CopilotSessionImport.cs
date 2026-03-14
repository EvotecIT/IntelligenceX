using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using IntelligenceX.Json;

namespace IntelligenceX.Telemetry.Usage.Copilot;

internal static class CopilotSessionImport {
    public const string StableProviderId = "copilot";
    private const string CliSurface = "cli";
    private const string CliErrorSurface = "cli-error";
    private const string CliSessionSummarySurface = "cli-session-summary";

    private sealed record TurnCandidate(
        string TurnId,
        string? InteractionId,
        DateTimeOffset StartedAtUtc);

    public static IReadOnlyList<UsageEventRecord> ParseFile(
        string filePath,
        SourceRootRecord root,
        string adapterId,
        string? machineId,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(filePath)) {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }
        if (root is null) {
            throw new ArgumentNullException(nameof(root));
        }
        if (string.IsNullOrWhiteSpace(adapterId)) {
            throw new ArgumentException("Adapter id is required.", nameof(adapterId));
        }

        var records = new List<UsageEventRecord>();
        var openTurns = new List<TurnCandidate>();
        var providerAccountId = CopilotSessionImportSupport.ResolveProviderAccountId(filePath, root.Path);
        var sessionId = default(string);
        var producer = default(string);
        var copilotVersion = default(string);
        var selectedModel = default(string);
        var resolvedLogModel = default(string);
        var sessionStartedAtUtc = default(DateTimeOffset?);
        var sequence = 0;
        var sawExactUsageEvent = false;
        JsonObject? sessionShutdownData = null;
        DateTimeOffset sessionShutdownTimestampUtc = default;
        string? sessionShutdownResponseId = null;

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

            if (!UsageTelemetryQuickReportSupport.TryParseTimestampUtc(entry.GetString("timestamp"), out var timestampUtc)) {
                continue;
            }

            var type = entry.GetString("type");
            var data = entry.GetObject("data");
            if (string.Equals(type, "session.start", StringComparison.OrdinalIgnoreCase)) {
                sessionId = CopilotSessionImportSupport.ExtractSessionId(data, filePath) ?? sessionId;
                producer = CopilotSessionImportSupport.ExtractProducer(data) ?? producer;
                copilotVersion = CopilotSessionImportSupport.ExtractCopilotVersion(data) ?? copilotVersion;
                selectedModel = CopilotSessionImportSupport.ExtractSelectedModel(data) ?? selectedModel;
                sessionStartedAtUtc = timestampUtc;
                resolvedLogModel ??= CopilotSessionImportSupport.ResolveDefaultModelFromLogs(filePath, root.Path, sessionId);
                continue;
            }

            if (string.Equals(type, "session.info", StringComparison.OrdinalIgnoreCase)) {
                providerAccountId = CopilotSessionImportSupport.ExtractAuthenticatedLogin(data) ?? providerAccountId;
                continue;
            }

            if (string.Equals(type, "session.error", StringComparison.OrdinalIgnoreCase)) {
                sessionId ??= CopilotSessionImportSupport.ExtractSessionId(data, filePath) ?? sessionId;
                var currentModel = CopilotSessionImportSupport.BuildEffectiveModelLabel(
                    selectedModel ?? resolvedLogModel,
                    producer,
                    copilotVersion);
                var errorRecord = new UsageEventRecord(
                    eventId: "uev_" + UsageTelemetryIdentity.ComputeStableHash(
                        StableProviderId + "|" +
                        UsageTelemetryIdentity.NormalizePath(filePath) + "|" +
                        "error|" +
                        (entry.GetString("id") ?? string.Empty)),
                    providerId: StableProviderId,
                    adapterId: adapterId,
                    sourceRootId: root.Id,
                    timestampUtc: timestampUtc);
                UsageTelemetryImportSupport.ApplyImportedEventMetadata(
                    errorRecord,
                    root,
                    machineId,
                    providerAccountId: providerAccountId,
                    sessionId: sessionId,
                    threadId: sessionId,
                    responseId: entry.GetString("id"),
                    model: currentModel,
                    surface: CliErrorSurface,
                    rawHash: UsageTelemetryIdentity.ComputeStableHash(rawLine),
                    truthLevel: UsageTruthLevel.Inferred);
                records.Add(errorRecord);
                continue;
            }

            if (string.Equals(type, "assistant.usage", StringComparison.OrdinalIgnoreCase)) {
                sessionId ??= CopilotSessionImportSupport.ExtractSessionId(data, filePath) ?? sessionId;
                selectedModel = CopilotSessionImportSupport.ExtractSelectedModel(data) ?? selectedModel;
                sawExactUsageEvent = true;

                var inputTokens = Math.Max(0L, UsageTelemetryQuickReportSupport.ReadInt64(data, "inputTokens", "input_tokens") ?? 0L);
                var outputTokens = Math.Max(0L, UsageTelemetryQuickReportSupport.ReadInt64(data, "outputTokens", "output_tokens") ?? 0L);
                var cachedInputTokens = Math.Max(0L, UsageTelemetryQuickReportSupport.ReadInt64(data, "cacheReadTokens", "cache_read_tokens") ?? 0L);
                var cacheWriteTokens = Math.Max(0L, UsageTelemetryQuickReportSupport.ReadInt64(data, "cacheWriteTokens", "cache_write_tokens") ?? 0L);
                var totalTokens = Math.Max(0L, inputTokens + outputTokens + cachedInputTokens + cacheWriteTokens);
                var currentModel = CopilotSessionImportSupport.BuildEffectiveModelLabel(
                    CopilotSessionImportSupport.ExtractSelectedModel(data) ?? selectedModel ?? resolvedLogModel,
                    producer,
                    copilotVersion);
                var usageRecord = new UsageEventRecord(
                    eventId: "uev_" + UsageTelemetryIdentity.ComputeStableHash(
                        StableProviderId + "|" +
                        UsageTelemetryIdentity.NormalizePath(filePath) + "|" +
                        "usage|" +
                        (entry.GetString("id") ?? string.Empty)),
                    providerId: StableProviderId,
                    adapterId: adapterId,
                    sourceRootId: root.Id,
                    timestampUtc: timestampUtc) {
                    InputTokens = inputTokens,
                    CachedInputTokens = cachedInputTokens,
                    OutputTokens = outputTokens,
                    TotalTokens = totalTokens
                };
                UsageTelemetryImportSupport.ApplyImportedEventMetadata(
                    usageRecord,
                    root,
                    machineId,
                    providerAccountId: providerAccountId,
                    sessionId: sessionId,
                    threadId: sessionId,
                    responseId: entry.GetString("apiCallId") ?? entry.GetString("id"),
                    model: currentModel,
                    surface: CliSurface,
                    durationMs: UsageTelemetryQuickReportSupport.ReadInt64(data, "duration"),
                    rawHash: UsageTelemetryIdentity.ComputeStableHash(rawLine),
                    truthLevel: UsageTruthLevel.Exact);
                records.Add(usageRecord);
                continue;
            }

            if (string.Equals(type, "session.shutdown", StringComparison.OrdinalIgnoreCase)) {
                sessionId ??= CopilotSessionImportSupport.ExtractSessionId(data, filePath) ?? sessionId;
                selectedModel = CopilotSessionImportSupport.ExtractSelectedModel(data) ?? selectedModel;
                sessionShutdownData = data;
                sessionShutdownTimestampUtc = timestampUtc;
                sessionShutdownResponseId = entry.GetString("id");
                continue;
            }

            if (string.Equals(type, "assistant.turn_start", StringComparison.OrdinalIgnoreCase)) {
                openTurns.Add(new TurnCandidate(
                    CopilotSessionImportSupport.ExtractTurnId(data) ?? "turn-" + openTurns.Count.ToString(CultureInfo.InvariantCulture),
                    CopilotSessionImportSupport.ExtractInteractionId(data),
                    timestampUtc));
                continue;
            }

            if (!string.Equals(type, "assistant.turn_end", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            sessionId ??= CopilotSessionImportSupport.ExtractSessionId(data, filePath) ?? sessionId;
            var turnId = CopilotSessionImportSupport.ExtractTurnId(data) ?? "turn-" + sequence.ToString(CultureInfo.InvariantCulture);
            var matchedTurn = FindMatchingTurn(openTurns, turnId);
            CopilotSessionImportSupport.TryComputeDurationMs(matchedTurn?.StartedAtUtc, timestampUtc, out var durationMs);

            sequence++;
            var model = CopilotSessionImportSupport.BuildEffectiveModelLabel(
                selectedModel ?? resolvedLogModel,
                producer,
                copilotVersion);
            var eventFingerprint =
                StableProviderId + "|" +
                UsageTelemetryIdentity.NormalizePath(filePath) + "|" +
                sequence.ToString(CultureInfo.InvariantCulture) + "|" +
                (sessionId ?? string.Empty) + "|" +
                turnId + "|" +
                timestampUtc.ToString("O", CultureInfo.InvariantCulture);
            var record = new UsageEventRecord(
                eventId: "uev_" + UsageTelemetryIdentity.ComputeStableHash(eventFingerprint),
                providerId: StableProviderId,
                adapterId: adapterId,
                sourceRootId: root.Id,
                timestampUtc: timestampUtc);
            UsageTelemetryImportSupport.ApplyImportedEventMetadata(
                record,
                root,
                machineId,
                providerAccountId: providerAccountId,
                sessionId: sessionId,
                threadId: sessionId,
                turnId: turnId,
                responseId: entry.GetString("id"),
                model: model,
                surface: CliSurface,
                durationMs: durationMs,
                rawHash: UsageTelemetryIdentity.ComputeStableHash(rawLine),
                truthLevel: UsageTruthLevel.Inferred);
            records.Add(record);
        }

        if (!sawExactUsageEvent && sessionShutdownData is not null) {
            records.AddRange(BuildSessionShutdownUsageRecords(
                sessionShutdownData,
                sessionShutdownTimestampUtc,
                sessionShutdownResponseId,
                filePath,
                root,
                adapterId,
                machineId,
                providerAccountId,
                sessionId,
                selectedModel ?? resolvedLogModel,
                producer,
                copilotVersion));
        }

        return records;
    }

    private static TurnCandidate? FindMatchingTurn(ICollection<TurnCandidate> openTurns, string turnId) {
        var matchedTurn = openTurns
            .Reverse()
            .FirstOrDefault(candidate => string.Equals(candidate.TurnId, turnId, StringComparison.OrdinalIgnoreCase))
            ?? openTurns.LastOrDefault();
        if (matchedTurn is null) {
            return null;
        }

        openTurns.Remove(matchedTurn);
        return matchedTurn;
    }

    private static IReadOnlyList<UsageEventRecord> BuildSessionShutdownUsageRecords(
        JsonObject data,
        DateTimeOffset timestampUtc,
        string? responseId,
        string filePath,
        SourceRootRecord root,
        string adapterId,
        string? machineId,
        string? providerAccountId,
        string? sessionId,
        string? selectedModel,
        string? producer,
        string? copilotVersion) {
        var modelMetrics = data.GetObject("modelMetrics");
        if (modelMetrics is null || modelMetrics.Count == 0) {
            return Array.Empty<UsageEventRecord>();
        }

        var currentModel = CopilotSessionImportSupport.BuildEffectiveModelLabel(selectedModel, producer, copilotVersion);
        var durationMs = UsageTelemetryQuickReportSupport.ReadInt64(data, "totalApiDurationMs", "total_api_duration_ms");
        var records = new List<UsageEventRecord>();
        foreach (var entry in modelMetrics) {
            var modelKey = UsageTelemetryQuickReportSupport.NormalizeOptional(entry.Key);
            var metric = entry.Value?.AsObject();
            var usage = metric?.GetObject("usage");
            if (string.IsNullOrWhiteSpace(modelKey) || usage is null) {
                continue;
            }

            var inputTokens = Math.Max(0L, UsageTelemetryQuickReportSupport.ReadInt64(usage, "inputTokens", "input_tokens") ?? 0L);
            var outputTokens = Math.Max(0L, UsageTelemetryQuickReportSupport.ReadInt64(usage, "outputTokens", "output_tokens") ?? 0L);
            var cachedInputTokens = Math.Max(0L, UsageTelemetryQuickReportSupport.ReadInt64(usage, "cacheReadTokens", "cache_read_tokens") ?? 0L);
            var cacheWriteTokens = Math.Max(0L, UsageTelemetryQuickReportSupport.ReadInt64(usage, "cacheWriteTokens", "cache_write_tokens") ?? 0L);
            var totalTokens = Math.Max(0L, inputTokens + outputTokens + cachedInputTokens + cacheWriteTokens);
            if (totalTokens <= 0L) {
                continue;
            }

            var record = new UsageEventRecord(
                eventId: "uev_" + UsageTelemetryIdentity.ComputeStableHash(
                    StableProviderId + "|" +
                    UsageTelemetryIdentity.NormalizePath(filePath) + "|" +
                    "shutdown|" +
                    (sessionId ?? string.Empty) + "|" +
                    modelKey),
                providerId: StableProviderId,
                adapterId: adapterId,
                sourceRootId: root.Id,
                timestampUtc: timestampUtc) {
                InputTokens = inputTokens,
                CachedInputTokens = cachedInputTokens,
                OutputTokens = outputTokens,
                TotalTokens = totalTokens
            };
            UsageTelemetryImportSupport.ApplyImportedEventMetadata(
                record,
                root,
                machineId,
                providerAccountId: providerAccountId,
                sessionId: sessionId,
                threadId: sessionId,
                responseId: responseId,
                model: UsageTelemetryQuickReportSupport.NormalizeOptional(modelKey) ?? currentModel,
                surface: CliSessionSummarySurface,
                durationMs: durationMs,
                rawHash: UsageTelemetryIdentity.ComputeStableHash(
                    (responseId ?? string.Empty) + "|" + modelKey + "|" + totalTokens.ToString(CultureInfo.InvariantCulture)),
                truthLevel: UsageTruthLevel.Exact);
            records.Add(record);
        }

        return records;
    }
}
