using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.Telemetry.Usage.Claude;

/// <summary>
/// Imports exact token usage from local Claude session JSONL files.
/// </summary>
public sealed class ClaudeSessionUsageAdapter : IUsageTelemetryAdapter {
    /// <summary>
    /// Stable adapter id for Claude session logs.
    /// </summary>
    public const string StableAdapterId = "claude.session-log";

    private const string CanonicalProviderId = "claude";
    private const string ParserVersion = "claude.session-log/v1";

    /// <inheritdoc />
    public string AdapterId => StableAdapterId;

    /// <inheritdoc />
    public bool CanImport(SourceRootRecord root) {
        if (root is null || !root.Enabled) {
            return false;
        }

        return IsClaudeProvider(root.ProviderId) &&
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
            throw new InvalidOperationException($"Root '{root.Path}' is not a supported Claude session source.");
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
                records.AddRange(ImportFile(filePath, root, context, artifact, cancellationToken));
                context.RawArtifactStore?.Upsert(artifact);
            } catch (IOException) {
                // Active Claude logs can be locked while the CLI is still writing them.
                continue;
            } catch (UnauthorizedAccessException) {
                // Recovered folders and cloud-synced roots may transiently deny access.
                continue;
            }
        }

        return Task.FromResult<IReadOnlyList<UsageEventRecord>>(records
            .OrderBy(record => record.TimestampUtc)
            .ThenBy(record => record.EventId, StringComparer.Ordinal)
            .ToArray());
    }

    private static IReadOnlyList<UsageEventRecord> ImportFile(
        string filePath,
        SourceRootRecord root,
        UsageImportContext context,
        RawArtifactDescriptor artifact,
        CancellationToken cancellationToken) {
        var candidates = new Dictionary<string, ClaudeUsageCandidate>(StringComparer.OrdinalIgnoreCase);
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
                // Claude logs can contain partial writes while sessions are active.
                continue;
            }

            if (!string.Equals(entry.GetString("type"), "assistant", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var message = entry.GetObject("message");
            var usage = NormalizeUsage(message?.GetObject("usage"));
            if (usage is null || usage.TotalTokens <= 0) {
                continue;
            }

            if (!TryParseTimestampUtc(entry.GetString("timestamp"), out var timestampUtc)) {
                continue;
            }

            var sessionId = NormalizeOptional(
                entry.GetString("sessionId")
                ?? entry.GetString("session_id")
                ?? entry.GetString("conversationId")
                ?? entry.GetString("conversation_id"));
            var requestId = NormalizeOptional(entry.GetString("requestId") ?? entry.GetString("request_id"));
            var messageId = NormalizeOptional(message?.GetString("id"));
            var key = BuildCandidateKey(messageId, requestId, lineNumber);

            var candidate = new ClaudeUsageCandidate(
                key,
                timestampUtc,
                sessionId,
                requestId,
                messageId,
                NormalizeOptional(message?.GetString("model")),
                usage,
                rawLine,
                lineNumber);

            if (candidates.TryGetValue(key, out var existing) &&
                !ShouldReplaceExisting(existing, candidate)) {
                continue;
            }

            candidates[key] = candidate;
        }

        var records = new List<UsageEventRecord>();
        foreach (var candidate in candidates.Values
                     .OrderBy(value => value.TimestampUtc)
                     .ThenBy(value => value.LineNumber)) {
            var turnId = candidate.MessageId ?? candidate.RequestId;
            var responseId = candidate.RequestId ?? candidate.MessageId;
            var eventFingerprint =
                $"{CanonicalProviderId}|{UsageTelemetryIdentity.NormalizePath(filePath)}|{candidate.Key}|{candidate.TimestampUtc:O}|{candidate.Usage.TotalTokens}";

            var record = new UsageEventRecord(
                eventId: "uev_" + UsageTelemetryIdentity.ComputeStableHash(eventFingerprint),
                providerId: CanonicalProviderId,
                adapterId: StableAdapterId,
                sourceRootId: root.Id,
                timestampUtc: candidate.TimestampUtc) {
                AccountLabel = NormalizeOptional(root.AccountHint),
                MachineId = NormalizeOptional(context.MachineId) ?? NormalizeOptional(root.MachineLabel),
                SessionId = candidate.SessionId,
                ThreadId = candidate.SessionId,
                TurnId = turnId,
                ResponseId = responseId,
                Model = candidate.Model,
                Surface = "cli",
                InputTokens = candidate.Usage.InputTokens,
                CachedInputTokens = candidate.Usage.CachedInputTokens,
                OutputTokens = candidate.Usage.OutputTokens,
                ReasoningTokens = candidate.Usage.ReasoningTokens,
                TotalTokens = candidate.Usage.TotalTokens,
                TruthLevel = UsageTruthLevel.Exact,
                RawHash = UsageTelemetryIdentity.ComputeStableHash(candidate.RawLine),
            };

            if (context.AccountResolver is not null) {
                var resolvedAccount = context.AccountResolver.Resolve(record, artifact);
                record.ProviderAccountId = NormalizeOptional(resolvedAccount.ProviderAccountId) ?? record.ProviderAccountId;
                record.AccountLabel = NormalizeOptional(resolvedAccount.AccountLabel) ?? record.AccountLabel;
                record.PersonLabel = NormalizeOptional(resolvedAccount.PersonLabel) ?? record.PersonLabel;
            }

            records.Add(record);
        }

        return records;
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string rootPath, bool preferRecentArtifacts) {
        if (File.Exists(rootPath)) {
            if (string.Equals(Path.GetExtension(rootPath), ".jsonl", StringComparison.OrdinalIgnoreCase)) {
                yield return Path.GetFullPath(rootPath);
            }
            yield break;
        }

        foreach (var directory in EnumerateCandidateDirectories(rootPath)) {
            var files = Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.AllDirectories)
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

        var normalizedRoot = Path.GetFullPath(rootPath);
        var rootName = Path.GetFileName(normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(rootName, "projects", StringComparison.OrdinalIgnoreCase)) {
            yield return normalizedRoot;
            yield break;
        }

        var projectsPath = Path.Combine(normalizedRoot, "projects");
        if (Directory.Exists(projectsPath)) {
            yield return projectsPath;
            yield break;
        }

        yield return normalizedRoot;
    }

    private static bool IsClaudeProvider(string? providerId) {
        var normalized = NormalizeOptional(providerId);
        return string.Equals(normalized, "claude", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "anthropic-claude", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "claude-code", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseTimestampUtc(string? value, out DateTimeOffset timestampUtc) {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestampUtc)) {
            timestampUtc = timestampUtc.ToUniversalTime();
            return true;
        }

        timestampUtc = default(DateTimeOffset);
        return false;
    }

    private static ClaudeNormalizedUsage? NormalizeUsage(JsonObject? usage) {
        if (usage is null) {
            return null;
        }

        var inputTokens = ReadInt64(usage, "input_tokens", "inputTokens") ?? 0;
        var cacheCreationTokens = ReadInt64(usage, "cache_creation_input_tokens", "cacheCreationInputTokens") ?? 0;
        var cacheReadTokens = ReadInt64(usage, "cache_read_input_tokens", "cacheReadInputTokens") ?? 0;
        var outputTokens = ReadInt64(usage, "output_tokens", "outputTokens") ?? 0;
        var reasoningTokens = ReadInt64(
                                  usage,
                                  "reasoning_tokens",
                                  "reasoningTokens")
                              ?? ReadInt64(
                                  usage.GetObject("output_tokens_details"),
                                  "reasoning_tokens",
                                  "reasoningTokens")
                              ?? 0;

        var totalTokens = ReadInt64(usage, "total_tokens", "totalTokens")
                          ?? Math.Max(0, inputTokens + cacheCreationTokens + cacheReadTokens + outputTokens);

        return new ClaudeNormalizedUsage(
            InputTokens: Math.Max(0, inputTokens + cacheCreationTokens),
            CachedInputTokens: Math.Max(0, cacheReadTokens),
            OutputTokens: Math.Max(0, outputTokens),
            ReasoningTokens: Math.Max(0, reasoningTokens),
            TotalTokens: Math.Max(0, totalTokens));
    }

    private static long? ReadInt64(JsonObject? obj, params string[] keys) {
        if (obj is null || keys is null || keys.Length == 0) {
            return null;
        }

        for (var i = 0; i < keys.Length; i++) {
            var value = obj.GetInt64(keys[i]);
            if (value.HasValue) {
                return value.Value;
            }

            var asDouble = obj.GetDouble(keys[i]);
            if (asDouble.HasValue) {
                return (long)Math.Round(asDouble.Value);
            }

            var asText = obj.GetString(keys[i]);
            if (long.TryParse(asText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) {
                return parsed;
            }
        }

        return null;
    }

    private static string BuildCandidateKey(string? messageId, string? requestId, int lineNumber) {
        if (!string.IsNullOrWhiteSpace(messageId) || !string.IsNullOrWhiteSpace(requestId)) {
            return (messageId ?? string.Empty) + "|" + (requestId ?? string.Empty);
        }

        return "line|" + lineNumber.ToString(CultureInfo.InvariantCulture);
    }

    private static bool ShouldReplaceExisting(ClaudeUsageCandidate existing, ClaudeUsageCandidate candidate) {
        if (candidate.Usage.TotalTokens > existing.Usage.TotalTokens) {
            return true;
        }

        if (candidate.Usage.TotalTokens < existing.Usage.TotalTokens) {
            return false;
        }

        if (candidate.TimestampUtc > existing.TimestampUtc) {
            return true;
        }

        return candidate.TimestampUtc == existing.TimestampUtc && candidate.LineNumber > existing.LineNumber;
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

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
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

    private sealed record ClaudeNormalizedUsage(
        long InputTokens,
        long CachedInputTokens,
        long OutputTokens,
        long ReasoningTokens,
        long TotalTokens);

    private sealed record ClaudeUsageCandidate(
        string Key,
        DateTimeOffset TimestampUtc,
        string? SessionId,
        string? RequestId,
        string? MessageId,
        string? Model,
        ClaudeNormalizedUsage Usage,
        string RawLine,
        int LineNumber);
}
