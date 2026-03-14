using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using IntelligenceX.Json;

namespace IntelligenceX.Telemetry.Usage.Claude;

internal static class ClaudeSessionImport {
    public const string StableProviderId = "claude";

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
                // Claude logs can contain partial writes while sessions are active.
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

            var sessionId = ClaudeSessionImportSupport.ExtractSessionId(entry);
            var requestId = ClaudeSessionImportSupport.ExtractRequestId(entry);
            var messageId = ClaudeSessionImportSupport.ExtractMessageId(message);
            var key = ClaudeSessionImportSupport.BuildCandidateKey(messageId, requestId, lineNumber);

            var candidate = new ClaudeUsageCandidate(
                key,
                timestampUtc,
                sessionId,
                requestId,
                messageId,
                ClaudeSessionImportSupport.ExtractModel(message),
                usage,
                rawLine,
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

        return candidates.Values
            .OrderBy(static value => value.TimestampUtc)
            .ThenBy(static value => value.LineNumber)
            .Select(candidate => {
                var turnId = candidate.MessageId ?? candidate.RequestId;
                var responseId = candidate.RequestId ?? candidate.MessageId;
                var eventFingerprint =
                    $"{StableProviderId}|{UsageTelemetryIdentity.NormalizePath(filePath)}|{candidate.Key}|{candidate.TimestampUtc:O}|{candidate.Usage.TotalTokens}";

                var record = new UsageEventRecord(
                    eventId: "uev_" + UsageTelemetryIdentity.ComputeStableHash(eventFingerprint),
                    providerId: StableProviderId,
                    adapterId: adapterId,
                    sourceRootId: root.Id,
                    timestampUtc: candidate.TimestampUtc) {
                    InputTokens = candidate.Usage.InputTokens,
                    CachedInputTokens = candidate.Usage.CachedInputTokens,
                    OutputTokens = candidate.Usage.OutputTokens,
                    ReasoningTokens = candidate.Usage.ReasoningTokens,
                    TotalTokens = candidate.Usage.TotalTokens,
                };
                UsageTelemetryImportSupport.ApplyImportedEventMetadata(
                    record,
                    root,
                    machineId,
                    sessionId: candidate.SessionId,
                    threadId: candidate.SessionId,
                    turnId: turnId,
                    responseId: responseId,
                    model: candidate.Model,
                    surface: "cli",
                    rawHash: UsageTelemetryIdentity.ComputeStableHash(candidate.RawLine));
                return record;
            })
            .ToArray();
    }

    private sealed record ClaudeUsageCandidate(
        string Key,
        DateTimeOffset TimestampUtc,
        string? SessionId,
        string? RequestId,
        string? MessageId,
        string? Model,
        ClaudeSessionImportSupport.ClaudeNormalizedUsage Usage,
        string RawLine,
        int LineNumber);
}
