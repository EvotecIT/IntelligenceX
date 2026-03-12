using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Imports exact token usage from local Codex session rollout files.
/// </summary>
public sealed class CodexSessionUsageAdapter : IUsageTelemetryAdapter {
    /// <summary>
    /// Stable adapter id for Codex rollout/session files.
    /// </summary>
    public const string StableAdapterId = "codex.session-log";

    private const string CanonicalProviderId = "codex";
    private const string ParserVersion = "codex.session-log/v1";

    /// <inheritdoc />
    public string AdapterId => StableAdapterId;

    /// <inheritdoc />
    public bool CanImport(SourceRootRecord root) {
        if (root is null || !root.Enabled) {
            return false;
        }

        return IsCodexProvider(root.ProviderId) &&
               (File.Exists(root.Path) || Directory.Exists(root.Path));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UsageEventRecord>> ImportAsync(
        SourceRootRecord root,
        UsageImportContext context,
        CancellationToken cancellationToken = default(CancellationToken)) {
        if (root is null) {
            throw new ArgumentNullException(nameof(root));
        }
        if (context is null) {
            throw new ArgumentNullException(nameof(context));
        }
        if (!CanImport(root)) {
            throw new InvalidOperationException($"Root '{root.Path}' is not a supported Codex session source.");
        }

        var records = new List<UsageEventRecord>();
        foreach (var filePath in EnumerateCandidateFiles(root.Path, context.PreferRecentArtifacts)) {
            cancellationToken.ThrowIfCancellationRequested();
            var artifact = RawArtifactDescriptor.CreateFile(
                root.Id,
                StableAdapterId,
                filePath,
                parserVersion: ParserVersion,
                importedAtUtc: context.UtcNow());
            if (ShouldSkipArtifact(context, artifact)) {
                continue;
            }
            if (!context.TryBeginArtifact()) {
                break;
            }

            try {
                ImportFile(filePath, root, context, artifact, records, cancellationToken);
                context.RawArtifactStore?.Upsert(artifact);
            } catch (IOException) {
                // Active session files can be locked while Codex is appending telemetry.
                continue;
            } catch (UnauthorizedAccessException) {
                // Recovered roots and partially migrated folders may briefly deny file access.
                continue;
            }
        }

        return Task.FromResult<IReadOnlyList<UsageEventRecord>>(records);
    }

    private static void ImportFile(
        string filePath,
        SourceRootRecord root,
        UsageImportContext context,
        RawArtifactDescriptor artifact,
        List<UsageEventRecord> records,
        CancellationToken cancellationToken) {
        var currentModel = default(string);
        var sessionId = TryExtractSessionIdFromFileName(filePath);
        var providerAccountId = ResolveProviderAccountId(filePath, root.Path);
        CodexNormalizedUsage? previousTotals = null;
        CodexNormalizedUsage? previousLastUsage = null;
        var lineNumber = 0;

        foreach (var rawLine in ReadLinesShared(filePath)) {
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
                sessionId = ExtractSessionId(payload) ?? sessionId;
                currentModel = ExtractModel(payload) ?? currentModel;
                continue;
            }

            if (string.Equals(type, "turn_context", StringComparison.OrdinalIgnoreCase)) {
                currentModel = ExtractModel(payload) ?? currentModel;
                continue;
            }

            if (!string.Equals(type, "event_msg", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(payload?.GetString("type"), "token_count", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var info = payload?.GetObject("info");
            var lastUsage = NormalizeUsage(info?.GetObject("last_token_usage") ?? info?.GetObject("lastTokenUsage"));
            var totalUsage = NormalizeUsage(info?.GetObject("total_token_usage") ?? info?.GetObject("totalTokenUsage"));

            if (totalUsage is not null && previousTotals is not null && totalUsage == previousTotals) {
                if (lastUsage is not null) {
                    previousLastUsage = lastUsage;
                }
                continue;
            }

            CodexNormalizedUsage? usage = null;
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
                usage = SubtractUsage(totalUsage, previousTotals);
            }

            if (totalUsage is not null) {
                previousTotals = totalUsage;
            }

            if (usage is null || usage.TotalTokens <= 0) {
                continue;
            }

            if (!TryParseTimestampUtc(entry.GetString("timestamp"), out var timestampUtc)) {
                continue;
            }

            var turnId = ExtractTurnId(payload, info);
            var responseId = ExtractResponseId(payload, info);
            var model = ExtractModel(payload) ?? currentModel;
            sessionId ??= ExtractSessionId(payload) ?? sessionId;

            var eventFingerprint = $"{CanonicalProviderId}|{UsageTelemetryIdentity.NormalizePath(filePath)}|{lineNumber}|{turnId}|{responseId}|{timestampUtc:O}|{usage.TotalTokens}";
            var rawHash = UsageTelemetryIdentity.ComputeStableHash(rawLine);
            var record = new UsageEventRecord(
                eventId: "uev_" + UsageTelemetryIdentity.ComputeStableHash(eventFingerprint),
                providerId: CanonicalProviderId,
                adapterId: StableAdapterId,
                sourceRootId: root.Id,
                timestampUtc: timestampUtc) {
                ProviderAccountId = providerAccountId,
                AccountLabel = NormalizeOptional(root.AccountHint),
                MachineId = NormalizeOptional(context.MachineId) ?? NormalizeOptional(root.MachineLabel),
                SessionId = sessionId,
                ThreadId = sessionId,
                TurnId = turnId,
                ResponseId = responseId,
                Model = model,
                Surface = "cli",
                InputTokens = usage.InputTokens,
                CachedInputTokens = usage.CachedInputTokens,
                OutputTokens = usage.OutputTokens,
                ReasoningTokens = usage.ReasoningTokens,
                TotalTokens = usage.TotalTokens,
                TruthLevel = UsageTruthLevel.Exact,
                RawHash = rawHash,
            };

            if (context.AccountResolver is not null) {
                var resolvedAccount = context.AccountResolver.Resolve(record, artifact);
                record.ProviderAccountId = NormalizeOptional(resolvedAccount.ProviderAccountId) ?? record.ProviderAccountId;
                record.AccountLabel = NormalizeOptional(resolvedAccount.AccountLabel) ?? record.AccountLabel;
                record.PersonLabel = NormalizeOptional(resolvedAccount.PersonLabel) ?? record.PersonLabel;
            }

            records.Add(record);
        }
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string rootPath, bool preferRecentArtifacts) {
        if (File.Exists(rootPath)) {
            if (string.Equals(Path.GetExtension(rootPath), ".jsonl", StringComparison.OrdinalIgnoreCase)) {
                yield return Path.GetFullPath(rootPath);
            }
            yield break;
        }

        var normalizedRoot = Path.GetFullPath(rootPath);
        var seenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in EnumerateCandidateDirectories(normalizedRoot)) {
            if (!seenDirectories.Add(directory)) {
                continue;
            }

            var files = Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.AllDirectories)
                .Where(IsSessionFile)
                .Select(Path.GetFullPath)
                .ToArray();
            var orderedFiles = preferRecentArtifacts
                ? files.OrderByDescending(GetLastWriteTimeUtcSafe)
                    .ThenBy(value => value, StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(value => value, StringComparer.OrdinalIgnoreCase);

            foreach (var file in orderedFiles) {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumerateCandidateDirectories(string rootPath) {
        if (!Directory.Exists(rootPath)) {
            yield break;
        }

        var rootName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(rootName, "sessions", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rootName, "archived_sessions", StringComparison.OrdinalIgnoreCase)) {
            yield return rootPath;
            yield break;
        }

        var sessionsPath = Path.Combine(rootPath, "sessions");
        var foundSpecificDirectory = false;
        if (Directory.Exists(sessionsPath)) {
            foundSpecificDirectory = true;
            yield return sessionsPath;
        }

        var archivedSessionsPath = Path.Combine(rootPath, "archived_sessions");
        if (Directory.Exists(archivedSessionsPath)) {
            foundSpecificDirectory = true;
            yield return archivedSessionsPath;
        }

        if (!foundSpecificDirectory) {
            yield return rootPath;
        }
    }

    private static bool IsCodexProvider(string? providerId) {
        var normalized = NormalizeOptional(providerId);
        return string.Equals(normalized, "codex", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "openai-codex", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "chatgpt-codex", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseTimestampUtc(string? value, out DateTimeOffset timestampUtc) {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestampUtc)) {
            timestampUtc = timestampUtc.ToUniversalTime();
            return true;
        }

        timestampUtc = default(DateTimeOffset);
        return false;
    }

    private static string? ExtractSessionId(JsonObject? payload) {
        if (payload is null) {
            return null;
        }

        var meta = payload.GetObject("meta");
        return NormalizeOptional(
            meta?.GetString("id")
            ?? meta?.GetString("conversation_id")
            ?? payload.GetString("session_id")
            ?? payload.GetString("sessionId")
            ?? payload.GetString("thread_id")
            ?? payload.GetString("threadId"));
    }

    private static string? TryExtractSessionIdFromFileName(string filePath) {
        var name = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        if (name.Length >= 36) {
            var suffix = name.Substring(name.Length - 36);
            if (Guid.TryParse(suffix, out _)) {
                return suffix;
            }
        }

        return NormalizeOptional(name);
    }

    private static string? ExtractTurnId(JsonObject? payload, JsonObject? info) {
        return NormalizeOptional(
            payload?.GetString("turn_id")
            ?? payload?.GetString("turnId")
            ?? info?.GetString("turn_id")
            ?? info?.GetString("turnId"));
    }

    private static string? ExtractResponseId(JsonObject? payload, JsonObject? info) {
        return NormalizeOptional(
            payload?.GetString("response_id")
            ?? payload?.GetString("responseId")
            ?? info?.GetString("response_id")
            ?? info?.GetString("responseId"));
    }

    private static string? ExtractModel(JsonObject? payload) {
        if (payload is null) {
            return null;
        }

        var directModel = NormalizeOptional(payload.GetString("model")) ?? NormalizeOptional(payload.GetString("model_name"));
        if (!string.IsNullOrWhiteSpace(directModel)) {
            return directModel;
        }

        var info = payload.GetObject("info");
        var infoModel = NormalizeOptional(info?.GetString("model")) ?? NormalizeOptional(info?.GetString("model_name"));
        if (!string.IsNullOrWhiteSpace(infoModel)) {
            return infoModel;
        }

        var infoMetadata = info?.GetObject("metadata");
        var infoMetadataModel = NormalizeOptional(infoMetadata?.GetString("model"));
        if (!string.IsNullOrWhiteSpace(infoMetadataModel)) {
            return infoMetadataModel;
        }

        var metadata = payload.GetObject("metadata");
        return NormalizeOptional(metadata?.GetString("model"));
    }

    private static CodexNormalizedUsage? NormalizeUsage(JsonObject? obj) {
        if (obj is null) {
            return null;
        }

        var inputTokens = ReadInt64(obj, "input_tokens", "inputTokens") ?? 0;
        var cachedInputTokens = ReadInt64(obj, "cached_input_tokens", "cachedInputTokens", "cache_read_input_tokens", "cacheReadInputTokens")
                                ?? 0;
        var outputTokens = ReadInt64(obj, "output_tokens", "outputTokens") ?? 0;
        var reasoningTokens = ReadInt64(obj, "reasoning_output_tokens", "reasoningOutputTokens", "reasoning_tokens", "reasoningTokens")
                              ?? 0;
        var totalTokens = ReadInt64(obj, "total_tokens", "totalTokens")
                          ?? Math.Max(0, inputTokens + outputTokens);

        return new CodexNormalizedUsage(
            InputTokens: Math.Max(0, inputTokens),
            CachedInputTokens: Math.Max(0, cachedInputTokens),
            OutputTokens: Math.Max(0, outputTokens),
            ReasoningTokens: Math.Max(0, reasoningTokens),
            TotalTokens: Math.Max(0, totalTokens));
    }

    private static CodexNormalizedUsage SubtractUsage(CodexNormalizedUsage current, CodexNormalizedUsage? previous) {
        return new CodexNormalizedUsage(
            InputTokens: Math.Max(0, current.InputTokens - (previous?.InputTokens ?? 0)),
            CachedInputTokens: Math.Max(0, current.CachedInputTokens - (previous?.CachedInputTokens ?? 0)),
            OutputTokens: Math.Max(0, current.OutputTokens - (previous?.OutputTokens ?? 0)),
            ReasoningTokens: Math.Max(0, current.ReasoningTokens - (previous?.ReasoningTokens ?? 0)),
            TotalTokens: Math.Max(0, current.TotalTokens - (previous?.TotalTokens ?? 0)));
    }

    private static long? ReadInt64(JsonObject? obj, params string[] keys) {
        if (obj is null || keys is null || keys.Length == 0) {
            return null;
        }

        for (var i = 0; i < keys.Length; i++) {
            var key = keys[i];
            var value = obj.GetInt64(key);
            if (value.HasValue) {
                return value.Value;
            }

            var asDouble = obj.GetDouble(key);
            if (asDouble.HasValue) {
                return (long)Math.Round(asDouble.Value);
            }

            var asText = obj.GetString(key);
            if (long.TryParse(asText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) {
                return parsed;
            }
        }

        return null;
    }

    private static bool IsSessionFile(string path) {
        var name = Path.GetFileName(path);
        return name.StartsWith("rollout-", StringComparison.OrdinalIgnoreCase) &&
               name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime GetLastWriteTimeUtcSafe(string path) {
        try {
            return File.GetLastWriteTimeUtc(path);
        } catch {
            return DateTime.MinValue;
        }
    }

    private static IEnumerable<string> ReadLinesShared(string filePath) {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (true) {
            var line = reader.ReadLine();
            if (line is null) {
                yield break;
            }

            yield return line;
        }
    }

    private static bool ShouldSkipArtifact(UsageImportContext context, RawArtifactDescriptor artifact) {
        if (context.ForceReimport || context.RawArtifactStore is null) {
            return false;
        }

        if (!context.RawArtifactStore.TryGet(artifact.SourceRootId, artifact.AdapterId, artifact.Path, out var existing)) {
            return false;
        }

        return string.Equals(existing.Fingerprint, artifact.Fingerprint, StringComparison.Ordinal) &&
               string.Equals(NormalizeOptional(existing.ParserVersion), NormalizeOptional(artifact.ParserVersion), StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveProviderAccountId(string artifactPath, string rootPath) {
        var candidateDirectories = new List<string>();
        AddSearchDirectory(candidateDirectories, Path.GetDirectoryName(Path.GetFullPath(artifactPath)));

        if (File.Exists(rootPath)) {
            AddSearchDirectory(candidateDirectories, Path.GetDirectoryName(Path.GetFullPath(rootPath)));
        } else if (Directory.Exists(rootPath)) {
            AddSearchDirectory(candidateDirectories, Path.GetFullPath(rootPath));
        }

        var seenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var startDirectory in candidateDirectories) {
            if (string.IsNullOrWhiteSpace(startDirectory)) {
                continue;
            }

            var current = startDirectory;
            for (var depth = 0; depth < 8 && !string.IsNullOrWhiteSpace(current); depth++) {
                if (!seenDirectories.Add(current)) {
                    break;
                }

                var authPath = Path.Combine(current, "auth.json");
                var accountId = TryReadAccountIdFromAuthJson(authPath);
                if (!string.IsNullOrWhiteSpace(accountId)) {
                    return accountId;
                }

                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) {
                    break;
                }

                current = parent;
            }
        }

        return null;
    }

    private static void AddSearchDirectory(List<string> directories, string? path) {
        if (!string.IsNullOrWhiteSpace(path)) {
            directories.Add(path!);
        }
    }

    private static string? TryReadAccountIdFromAuthJson(string authPath) {
        if (!File.Exists(authPath)) {
            return null;
        }

        try {
            var root = JsonLite.Parse(File.ReadAllText(authPath)).AsObject();
            var tokens = root?.GetObject("tokens");
            var directAccountId = NormalizeOptional(tokens?.GetString("account_id"));
            if (!string.IsNullOrWhiteSpace(directAccountId)) {
                return directAccountId;
            }

            var accessToken = NormalizeOptional(tokens?.GetString("access_token"));
            if (!string.IsNullOrWhiteSpace(accessToken)) {
                return NormalizeOptional(JwtDecoder.TryGetAccountId(accessToken!));
            }
        } catch {
            // Best-effort account discovery for recovered roots.
        }

        return null;
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private sealed record CodexNormalizedUsage(
        long InputTokens,
        long CachedInputTokens,
        long OutputTokens,
        long ReasoningTokens,
        long TotalTokens);
}
