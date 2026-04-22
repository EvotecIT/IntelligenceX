using System;
using System.Collections.Generic;
using System.Threading;
using IntelligenceX.Json;

namespace IntelligenceX.Telemetry.Usage.Codex;

internal static class CodexSessionImport {
    public const string StableProviderId = "codex";

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
        var currentModel = default(string);
        var conversationTitle = default(string);
        var workspacePath = default(string);
        var repositoryName = default(string);
        var sessionId = CodexSessionImportSupport.TryExtractSessionIdFromFileName(filePath);
        var resolvedAccount = CodexSessionImportSupport.ResolveAccount(filePath, root.Path);
        CodexSessionImportSupport.CodexNormalizedUsage? previousTotals = null;
        CodexSessionImportSupport.CodexNormalizedUsage? previousLastUsage = null;
        var pendingCompactCount = 0;
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
                // Recovered folders and partially-written logs are expected; skip unreadable lines.
                continue;
            }

            var type = entry.GetString("type");
            var payload = entry.GetObject("payload");
            if (string.Equals(type, "session_meta", StringComparison.OrdinalIgnoreCase)) {
                sessionId = CodexSessionImportSupport.ExtractSessionId(payload) ?? sessionId;
                currentModel = CodexSessionImportSupport.ExtractModel(payload) ?? currentModel;
                workspacePath = NormalizeOptional(payload?.GetString("cwd")) ?? workspacePath;
                repositoryName = ExtractRepositoryName(payload?.GetObject("git")) ?? repositoryName;
                continue;
            }

            if (string.Equals(type, "turn_context", StringComparison.OrdinalIgnoreCase)) {
                currentModel = CodexSessionImportSupport.ExtractModel(payload) ?? currentModel;
                workspacePath = NormalizeOptional(payload?.GetString("cwd")) ?? workspacePath;
                continue;
            }

            if (!string.Equals(type, "event_msg", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var payloadType = payload?.GetString("type");
            if (string.Equals(payloadType, "context_compacted", StringComparison.OrdinalIgnoreCase)) {
                pendingCompactCount++;
                continue;
            }

            if (string.Equals(payloadType, "thread_name_updated", StringComparison.OrdinalIgnoreCase)) {
                sessionId = NormalizeOptional(payload?.GetString("thread_id"))
                            ?? NormalizeOptional(payload?.GetString("threadId"))
                            ?? sessionId;
                conversationTitle = NormalizeOptional(payload?.GetString("thread_name"))
                                    ?? NormalizeOptional(payload?.GetString("threadName"))
                                    ?? conversationTitle;
                continue;
            }

            if (!string.Equals(payloadType, "token_count", StringComparison.OrdinalIgnoreCase)) {
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

            var turnId = CodexSessionImportSupport.ExtractTurnId(payload, info);
            var responseId = CodexSessionImportSupport.ExtractResponseId(payload, info);
            var model = CodexSessionImportSupport.ExtractModel(payload) ?? currentModel;
            sessionId ??= CodexSessionImportSupport.ExtractSessionId(payload) ?? sessionId;

            var rawHash = UsageTelemetryIdentity.ComputeStableHash(rawLine);
            var identityScope = UsageTelemetryQuickReportSupport.NormalizeOptional(sessionId)
                                ?? UsageTelemetryQuickReportSupport.NormalizeOptional(responseId)
                                ?? UsageTelemetryIdentity.NormalizePath(filePath);
            var eventFingerprint = $"{StableProviderId}|{identityScope}|{turnId}|{responseId}|{timestampUtc:O}|{rawHash}";
            var record = new UsageEventRecord(
                eventId: "uev_" + UsageTelemetryIdentity.ComputeStableHash(eventFingerprint),
                providerId: StableProviderId,
                adapterId: adapterId,
                sourceRootId: root.Id,
                timestampUtc: timestampUtc) {
                InputTokens = usage.InputTokens,
                CachedInputTokens = usage.CachedInputTokens,
                OutputTokens = usage.OutputTokens,
                ReasoningTokens = usage.ReasoningTokens,
                TotalTokens = usage.TotalTokens,
                CompactCount = pendingCompactCount > 0 ? pendingCompactCount : null,
            };
            pendingCompactCount = 0;
            UsageTelemetryImportSupport.ApplyImportedEventMetadata(
                record,
                root,
                machineId,
                providerAccountId: resolvedAccount.ProviderAccountId,
                accountLabel: resolvedAccount.AccountLabel,
                personLabel: resolvedAccount.PersonLabel,
                sessionId: sessionId,
                threadId: sessionId,
                turnId: turnId,
                responseId: responseId,
                model: model,
                surface: "cli",
                rawHash: rawHash);
            record.ConversationTitle = conversationTitle;
            record.WorkspacePath = workspacePath;
            record.RepositoryName = repositoryName;
            records.Add(record);
        }

        foreach (var record in records) {
            record.ConversationTitle ??= conversationTitle;
            record.WorkspacePath ??= workspacePath;
            record.RepositoryName ??= repositoryName;
        }

        return records;
    }

    private static string? ExtractRepositoryName(JsonObject? git) {
        var repositoryUrl = NormalizeOptional(git?.GetString("repository_url"))
                            ?? NormalizeOptional(git?.GetString("repositoryUrl"));
        if (repositoryUrl is null) {
            return null;
        }

        var normalized = repositoryUrl.Trim().TrimEnd('/');
        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized.Substring(0, normalized.Length - 4);
        }

        string? path = null;
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri)) {
            path = uri.AbsolutePath;
        } else {
            var colonIndex = normalized.IndexOf(':');
            if (colonIndex >= 0 && !LooksLikeWindowsDrive(normalized, colonIndex)) {
                path = normalized.Substring(colonIndex + 1);
            }
        }

        path ??= normalized;
        path = path.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }

        var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2) {
            return parts[parts.Length - 2] + "/" + parts[parts.Length - 1];
        }

        return parts.Length == 1 ? parts[0] : null;
    }

    private static bool LooksLikeWindowsDrive(string value, int colonIndex) {
        return colonIndex == 1 && value.Length > 1 && char.IsLetter(value[0]);
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
