using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using IntelligenceX.Json;

namespace IntelligenceX.Telemetry.Usage.Copilot;

internal static class CopilotSessionImport {
    public const string StableProviderId = "copilot";

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
        var sequence = 0;

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
                continue;
            }

            if (string.Equals(type, "session.info", StringComparison.OrdinalIgnoreCase)) {
                providerAccountId = CopilotSessionImportSupport.ExtractAuthenticatedLogin(data) ?? providerAccountId;
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
            var model = CopilotSessionImportSupport.BuildDefaultModelLabel(producer, copilotVersion);
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
                surface: "cli",
                durationMs: durationMs,
                rawHash: UsageTelemetryIdentity.ComputeStableHash(rawLine),
                truthLevel: UsageTruthLevel.Inferred);
            records.Add(record);
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
}
