using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using IntelligenceX.Json;

namespace IntelligenceX.Telemetry.Usage.LmStudio;

internal static class LmStudioConversationImport {
    public const string StableProviderId = "lmstudio";

    public static bool IsLmStudioProvider(string? providerId) {
        return LmStudioConversationImportSupport.IsLmStudioProvider(providerId);
    }

    public static IEnumerable<string> EnumerateCandidateFiles(string rootPath, bool preferRecentArtifacts) {
        foreach (var file in LmStudioConversationImportSupport.EnumerateCandidateFiles(rootPath, preferRecentArtifacts)) {
            yield return file;
        }
    }

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

        var content = LmStudioConversationImportSupport.ReadAllTextShared(filePath);
        var conversation = JsonLite.Parse(content).AsObject()
                           ?? throw new FormatException("LM Studio conversation did not contain a JSON object.");

        var normalizedPath = UsageTelemetryIdentity.NormalizePath(filePath);
        var sessionId = LmStudioConversationImportSupport.NormalizeOptional(LmStudioConversationImportSupport.ExtractSessionId(filePath))
                        ?? LmStudioConversationImportSupport.NormalizeOptional(conversation.GetString("name"))
                        ?? "lmstudio-session";
        var conversationTitle = LmStudioConversationImportSupport.NormalizeOptional(conversation.GetString("name"));
        var fallbackTimestampUtc = LmStudioConversationImportSupport.TryReadUnixTimeMilliseconds(conversation, "assistantLastMessagedAt")
                                   ?? LmStudioConversationImportSupport.TryReadUnixTimeMilliseconds(conversation, "userLastMessagedAt")
                                   ?? LmStudioConversationImportSupport.TryReadUnixTimeMilliseconds(conversation, "createdAt")
                                   ?? new DateTimeOffset(LmStudioConversationImportSupport.GetLastWriteTimeUtcSafe(filePath), TimeSpan.Zero);
        var fallbackModel = LmStudioConversationImportSupport.NormalizeOptional(
            conversation.GetObject("lastUsedModel")?.GetString("identifier")
            ?? conversation.GetObject("lastUsedModel")?.GetString("indexedModelIdentifier"));
        var messages = conversation.GetArray("messages");
        if (messages is null || messages.Count == 0) {
            return Array.Empty<UsageEventRecord>();
        }

        var records = new List<UsageEventRecord>();
        for (var messageIndex = 0; messageIndex < messages.Count; messageIndex++) {
            cancellationToken.ThrowIfCancellationRequested();
            var message = messages[messageIndex].AsObject();
            if (message is null) {
                continue;
            }

            var selectedVersion = LmStudioConversationImportSupport.GetSelectedVersion(message);
            if (selectedVersion is null ||
                !string.Equals(selectedVersion.GetString("role"), "assistant", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var senderName = LmStudioConversationImportSupport.NormalizeOptional(selectedVersion.GetObject("senderInfo")?.GetString("senderName"));
            var steps = selectedVersion.GetArray("steps");
            if (steps is null || steps.Count == 0) {
                continue;
            }

            var turnId = "message-" + (messageIndex + 1).ToString(CultureInfo.InvariantCulture);
            for (var stepIndex = 0; stepIndex < steps.Count; stepIndex++) {
                cancellationToken.ThrowIfCancellationRequested();
                var step = steps[stepIndex].AsObject();
                if (step is null) {
                    continue;
                }

                var genInfo = step.GetObject("genInfo");
                var stats = genInfo?.GetObject("stats");
                var usage = LmStudioConversationImportSupport.NormalizeUsage(stats);
                if (!usage.HasValue || usage.Value.TotalTokens <= 0) {
                    continue;
                }
                var usageValue = usage.Value;

                var stepIdentifier = LmStudioConversationImportSupport.NormalizeOptional(step.GetString("stepIdentifier"))
                                     ?? turnId + "-step-" + (stepIndex + 1).ToString(CultureInfo.InvariantCulture);
                var timestampUtc = LmStudioConversationImportSupport.TryParseStepTimestampUtc(stepIdentifier, out var parsedTimestampUtc)
                    ? parsedTimestampUtc
                    : fallbackTimestampUtc;
                var model = LmStudioConversationImportSupport.NormalizeOptional(
                    genInfo?.GetString("identifier")
                    ?? genInfo?.GetString("indexedModelIdentifier")
                    ?? senderName
                    ?? fallbackModel);
                var rawPayload = JsonLite.Serialize(JsonValue.From(step));
                var eventFingerprint = StableProviderId + "|" + normalizedPath + "|" + stepIdentifier + "|" +
                                       timestampUtc.ToString("O", CultureInfo.InvariantCulture) + "|" +
                                       usageValue.TotalTokens.ToString(CultureInfo.InvariantCulture);
                var record = new UsageEventRecord(
                    eventId: "uev_" + UsageTelemetryIdentity.ComputeStableHash(eventFingerprint),
                    providerId: StableProviderId,
                    adapterId: adapterId,
                    sourceRootId: root.Id,
                    timestampUtc: timestampUtc) {
                    InputTokens = usageValue.InputTokens,
                    OutputTokens = usageValue.OutputTokens,
                    TotalTokens = usageValue.TotalTokens,
                };
                UsageTelemetryImportSupport.ApplyImportedEventMetadata(
                    record,
                    root,
                    machineId,
                    sessionId: sessionId,
                    threadId: sessionId,
                    turnId: turnId,
                    responseId: stepIdentifier,
                    model: model,
                    surface: "chat",
                    durationMs: usageValue.DurationMs,
                    rawHash: UsageTelemetryIdentity.ComputeStableHash(rawPayload));
                record.ConversationTitle = conversationTitle;
                records.Add(record);
            }
        }

        return records
            .OrderBy(record => record.TimestampUtc)
            .ThenBy(record => record.EventId, StringComparer.Ordinal)
            .ToArray();
    }
}
