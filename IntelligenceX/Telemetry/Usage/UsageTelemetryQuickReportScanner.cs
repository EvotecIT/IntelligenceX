using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Configures the lightweight quick-scan path used by telemetry usage reports.
/// </summary>
public sealed class UsageTelemetryQuickReportOptions {
    /// <summary>
    /// Limits the scan to a single provider when specified.
    /// </summary>
    public string? ProviderId { get; set; }

    /// <summary>
    /// Overrides the machine identifier stamped onto synthesized usage records.
    /// </summary>
    public string? MachineId { get; set; }

    /// <summary>
    /// Optional raw-artifact cache used to reuse per-file quick-scan summaries.
    /// </summary>
    public IRawArtifactStore? RawArtifactStore { get; set; }

    /// <summary>
    /// Prefers recently-modified artifacts before older files.
    /// </summary>
    public bool PreferRecentArtifacts { get; set; } = true;

    /// <summary>
    /// Forces reparsing even when a cached quick-scan summary is available.
    /// </summary>
    public bool ForceReimport { get; set; }

    /// <summary>
    /// Caps the number of artifacts parsed or reused during the scan.
    /// </summary>
    public int? MaxArtifacts { get; set; }

    /// <summary>
    /// Receives progress notifications while roots and artifacts are processed.
    /// </summary>
    public Action<UsageImportProgressUpdate>? Progress { get; set; }

    /// <summary>
    /// Supplies the current UTC timestamp for cache bookkeeping.
    /// </summary>
    public Func<DateTimeOffset> UtcNow { get; set; } = static () => DateTimeOffset.UtcNow;
}

/// <summary>
/// Captures the synthesized usage records and cache statistics produced by a quick scan.
/// </summary>
public sealed class UsageTelemetryQuickReportResult {
    /// <summary>
    /// Gets the merged usage records produced from the scanned artifacts.
    /// </summary>
    public List<UsageEventRecord> Events { get; } = new();

    /// <summary>
    /// Gets or sets the number of source roots considered by the scan.
    /// </summary>
    public int RootsConsidered { get; set; }

    /// <summary>
    /// Gets or sets the number of artifacts parsed from disk.
    /// </summary>
    public int ArtifactsParsed { get; set; }

    /// <summary>
    /// Gets or sets the number of artifacts reused from the quick-scan cache.
    /// </summary>
    public int ArtifactsReused { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the artifact cap stopped the scan early.
    /// </summary>
    public bool ArtifactBudgetReached { get; set; }
}

/// <summary>
/// Produces report-ready usage aggregates directly from provider artifacts without full ledger import.
/// </summary>
public sealed class UsageTelemetryQuickReportScanner {
    private const string CodexAdapterId = "codex.quick-report";
    private const string CodexParserVersion = "codex.quick-report/v1";
    private const string ClaudeAdapterId = "claude.quick-report";
    private const string ClaudeParserVersion = "claude.quick-report/v1";

    /// <summary>
    /// Scans the supplied roots and returns merged report records plus cache statistics.
    /// </summary>
    public Task<UsageTelemetryQuickReportResult> ScanAsync(
        IEnumerable<SourceRootRecord> roots,
        UsageTelemetryQuickReportOptions? options = null,
        CancellationToken cancellationToken = default) {
        if (roots is null) {
            throw new ArgumentNullException(nameof(roots));
        }

        var effectiveOptions = options ?? new UsageTelemetryQuickReportOptions();
        var result = new UsageTelemetryQuickReportResult();
        var allRecords = new List<UsageEventRecord>();
        var parsedArtifacts = 0;

        foreach (var root in roots.Where(static root => root is not null && root.Enabled)) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MatchesProvider(root.ProviderId, effectiveOptions.ProviderId)) {
                continue;
            }

            result.RootsConsidered++;
            effectiveOptions.Progress?.Invoke(new UsageImportProgressUpdate {
                Phase = "root",
                ProviderId = root.ProviderId,
                RootId = root.Id,
                RootPath = root.Path,
                Message = "Quick scanning " + root.ProviderId + " root: " + root.Path
            });

            if (IsCodexProvider(root.ProviderId)) {
                ScanCodexRoot(root, effectiveOptions, allRecords, ref parsedArtifacts, result, cancellationToken);
            } else if (IsClaudeProvider(root.ProviderId)) {
                ScanClaudeRoot(root, effectiveOptions, allRecords, ref parsedArtifacts, result, cancellationToken);
            }

            if (result.ArtifactBudgetReached) {
                break;
            }
        }

        result.Events.AddRange(MergeRecords(allRecords));
        return Task.FromResult(result);
    }

    private static void ScanCodexRoot(
        SourceRootRecord root,
        UsageTelemetryQuickReportOptions options,
        List<UsageEventRecord> allRecords,
        ref int parsedArtifacts,
        UsageTelemetryQuickReportResult result,
        CancellationToken cancellationToken) {
        foreach (var filePath in EnumerateCodexCandidateFiles(root.Path, options.PreferRecentArtifacts)) {
            cancellationToken.ThrowIfCancellationRequested();
            var artifact = RawArtifactDescriptor.CreateFile(root.Id, CodexAdapterId, filePath, CodexParserVersion, options.UtcNow());
            if (TryReuseCachedQuickRecords(options, artifact, out var cachedRecords)) {
                result.ArtifactsReused++;
                allRecords.AddRange(cachedRecords);
                continue;
            }
            if (!TryReserveArtifactBudget(options, ref parsedArtifacts, result)) {
                break;
            }

            var records = ParseCodexFile(root, filePath, options, cancellationToken);
            artifact.ParsedBytes = artifact.SizeBytes;
            artifact.StateJson = SerializeQuickRecords(records);
            options.RawArtifactStore?.Upsert(artifact);
            result.ArtifactsParsed++;
            allRecords.AddRange(records);
        }
    }

    private static void ScanClaudeRoot(
        SourceRootRecord root,
        UsageTelemetryQuickReportOptions options,
        List<UsageEventRecord> allRecords,
        ref int parsedArtifacts,
        UsageTelemetryQuickReportResult result,
        CancellationToken cancellationToken) {
        foreach (var filePath in EnumerateClaudeCandidateFiles(root.Path, options.PreferRecentArtifacts)) {
            cancellationToken.ThrowIfCancellationRequested();
            var artifact = RawArtifactDescriptor.CreateFile(root.Id, ClaudeAdapterId, filePath, ClaudeParserVersion, options.UtcNow());
            if (TryReuseCachedQuickRecords(options, artifact, out var cachedRecords)) {
                result.ArtifactsReused++;
                allRecords.AddRange(cachedRecords);
                continue;
            }
            if (!TryReserveArtifactBudget(options, ref parsedArtifacts, result)) {
                break;
            }

            var records = ParseClaudeFile(root, filePath, options, cancellationToken);
            artifact.ParsedBytes = artifact.SizeBytes;
            artifact.StateJson = SerializeQuickRecords(records);
            options.RawArtifactStore?.Upsert(artifact);
            result.ArtifactsParsed++;
            allRecords.AddRange(records);
        }
    }

    private static bool TryReuseCachedQuickRecords(
        UsageTelemetryQuickReportOptions options,
        RawArtifactDescriptor artifact,
        out IReadOnlyList<UsageEventRecord> records) {
        records = Array.Empty<UsageEventRecord>();
        if (options.ForceReimport || options.RawArtifactStore is null) {
            return false;
        }
        if (!options.RawArtifactStore.TryGet(artifact.SourceRootId, artifact.AdapterId, artifact.Path, out var existing)) {
            return false;
        }
        if (!string.Equals(existing.Fingerprint, artifact.Fingerprint, StringComparison.Ordinal) ||
            !string.Equals(NormalizeOptional(existing.ParserVersion), NormalizeOptional(artifact.ParserVersion), StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(existing.StateJson)) {
            return false;
        }

        records = DeserializeQuickRecords(existing.StateJson!);
        return true;
    }

    private static bool TryReserveArtifactBudget(
        UsageTelemetryQuickReportOptions options,
        ref int parsedArtifacts,
        UsageTelemetryQuickReportResult result) {
        if (!options.MaxArtifacts.HasValue) {
            parsedArtifacts++;
            return true;
        }
        if (parsedArtifacts >= options.MaxArtifacts.Value) {
            result.ArtifactBudgetReached = true;
            return false;
        }
        parsedArtifacts++;
        return true;
    }

    private static bool MatchesProvider(string providerId, string? filter) {
        var normalizedFilter = NormalizeOptional(filter);
        if (normalizedFilter is null) {
            return true;
        }

        return string.Equals(providerId, normalizedFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCodexProvider(string? providerId) {
        var normalized = NormalizeOptional(providerId);
        return string.Equals(normalized, "codex", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "openai-codex", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "chatgpt-codex", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsClaudeProvider(string? providerId) {
        var normalized = NormalizeOptional(providerId);
        return string.Equals(normalized, "claude", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "anthropic-claude", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "claude-code", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string SerializeQuickRecords(IReadOnlyList<UsageEventRecord> records) {
        var array = new JsonArray();
        foreach (var record in records) {
            var obj = new JsonObject()
                .Add("eventId", record.EventId)
                .Add("providerId", record.ProviderId)
                .Add("adapterId", record.AdapterId)
                .Add("sourceRootId", record.SourceRootId)
                .Add("timestampUtc", record.TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                .Add("accountLabel", record.AccountLabel)
                .Add("machineId", record.MachineId)
                .Add("model", record.Model)
                .Add("surface", record.Surface);

            AddOptionalInt64(obj, "inputTokens", record.InputTokens);
            AddOptionalInt64(obj, "cachedInputTokens", record.CachedInputTokens);
            AddOptionalInt64(obj, "outputTokens", record.OutputTokens);
            AddOptionalInt64(obj, "reasoningTokens", record.ReasoningTokens);
            AddOptionalInt64(obj, "totalTokens", record.TotalTokens);

            array.Add(obj);
        }

        return JsonLite.Serialize(JsonValue.From(new JsonObject()
            .Add("version", "quick-report/v1")
            .Add("records", array)));
    }

    private static IReadOnlyList<UsageEventRecord> DeserializeQuickRecords(string stateJson) {
        var root = JsonLite.Parse(stateJson).AsObject();
        var records = new List<UsageEventRecord>();
        foreach (var item in root?.GetArray("records") ?? new JsonArray()) {
            var obj = item.AsObject();
            if (obj is null) {
                continue;
            }

            if (!DateTimeOffset.TryParse(obj.GetString("timestampUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestampUtc)) {
                continue;
            }

            records.Add(new UsageEventRecord(
                obj.GetString("eventId") ?? "uev_" + Guid.NewGuid().ToString("N"),
                obj.GetString("providerId") ?? "unknown-provider",
                obj.GetString("adapterId") ?? "quick-report",
                obj.GetString("sourceRootId") ?? "unknown-root",
                timestampUtc.ToUniversalTime()) {
                AccountLabel = NormalizeOptional(obj.GetString("accountLabel")),
                MachineId = NormalizeOptional(obj.GetString("machineId")),
                Model = NormalizeOptional(obj.GetString("model")),
                Surface = NormalizeOptional(obj.GetString("surface")),
                InputTokens = obj.GetInt64("inputTokens"),
                CachedInputTokens = obj.GetInt64("cachedInputTokens"),
                OutputTokens = obj.GetInt64("outputTokens"),
                ReasoningTokens = obj.GetInt64("reasoningTokens"),
                TotalTokens = obj.GetInt64("totalTokens"),
                TruthLevel = UsageTruthLevel.Exact
            });
        }

        return records;
    }

    private static void AddOptionalInt64(JsonObject obj, string key, long? value) {
        if (!value.HasValue) {
            return;
        }

        obj.Add(key, value.Value);
    }

    private static IReadOnlyList<UsageEventRecord> MergeRecords(IEnumerable<UsageEventRecord> records) {
        return records
            .GroupBy(record => string.Join("|",
                record.ProviderId,
                record.SourceRootId,
                record.TimestampUtc.UtcDateTime.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                NormalizeOptional(record.Model) ?? string.Empty,
                NormalizeOptional(record.Surface) ?? string.Empty,
                NormalizeOptional(record.AccountLabel) ?? string.Empty),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => {
                var first = group.First();
                return new UsageEventRecord(
                    "uev_" + UsageTelemetryIdentity.ComputeStableHash(group.Key),
                    first.ProviderId,
                    first.AdapterId,
                    first.SourceRootId,
                    first.TimestampUtc) {
                    AccountLabel = first.AccountLabel,
                    MachineId = first.MachineId,
                    Model = first.Model,
                    Surface = first.Surface,
                    InputTokens = group.Sum(static item => item.InputTokens ?? 0L),
                    CachedInputTokens = group.Sum(static item => item.CachedInputTokens ?? 0L),
                    OutputTokens = group.Sum(static item => item.OutputTokens ?? 0L),
                    ReasoningTokens = group.Sum(static item => item.ReasoningTokens ?? 0L),
                    TotalTokens = group.Sum(static item => item.TotalTokens ?? 0L),
                    TruthLevel = UsageTruthLevel.Exact
                };
            })
            .OrderBy(static record => record.TimestampUtc)
            .ThenBy(static record => record.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static record => record.Model, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddAggregate(
        Dictionary<string, UsageAggregateBucket> aggregates,
        string providerId,
        string sourceRootId,
        DateTime dayUtc,
        string model,
        string surface,
        string? machineId,
        string? accountLabel,
        long inputTokens,
        long cachedInputTokens,
        long outputTokens,
        long reasoningTokens,
        long totalTokens) {
        var normalizedModel = NormalizeOptional(model) ?? "unknown-model";
        var normalizedSurface = NormalizeOptional(surface) ?? "unknown-surface";
        var key = string.Join("|", providerId, sourceRootId, dayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), normalizedModel, normalizedSurface, NormalizeOptional(accountLabel) ?? string.Empty);
        if (!aggregates.TryGetValue(key, out var bucket)) {
            bucket = new UsageAggregateBucket(providerId, sourceRootId, dayUtc, normalizedModel, normalizedSurface, machineId, accountLabel);
            aggregates[key] = bucket;
        }

        bucket.InputTokens += Math.Max(0L, inputTokens);
        bucket.CachedInputTokens += Math.Max(0L, cachedInputTokens);
        bucket.OutputTokens += Math.Max(0L, outputTokens);
        bucket.ReasoningTokens += Math.Max(0L, reasoningTokens);
        bucket.TotalTokens += Math.Max(0L, totalTokens);
    }

    private static IReadOnlyList<UsageEventRecord> FinalizeAggregates(
        Dictionary<string, UsageAggregateBucket> aggregates,
        string adapterId) {
        return aggregates.Values
            .OrderBy(static value => value.DayUtc)
            .ThenBy(static value => value.Model, StringComparer.OrdinalIgnoreCase)
            .Select(bucket => new UsageEventRecord(
                "uev_" + UsageTelemetryIdentity.ComputeStableHash(
                    bucket.ProviderId + "|" +
                    bucket.SourceRootId + "|" +
                    bucket.DayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|" +
                    bucket.Model + "|" +
                    bucket.Surface + "|" +
                    (bucket.AccountLabel ?? string.Empty)),
                bucket.ProviderId,
                adapterId,
                bucket.SourceRootId,
                new DateTimeOffset(bucket.DayUtc, TimeSpan.Zero)) {
                AccountLabel = bucket.AccountLabel,
                MachineId = bucket.MachineId,
                Model = bucket.Model,
                Surface = bucket.Surface,
                InputTokens = bucket.InputTokens,
                CachedInputTokens = bucket.CachedInputTokens,
                OutputTokens = bucket.OutputTokens,
                ReasoningTokens = bucket.ReasoningTokens,
                TotalTokens = bucket.TotalTokens,
                TruthLevel = UsageTruthLevel.Exact
            })
            .ToArray();
    }

    private sealed class UsageAggregateBucket {
        public UsageAggregateBucket(
            string providerId,
            string sourceRootId,
            DateTime dayUtc,
            string model,
            string surface,
            string? machineId,
            string? accountLabel) {
            ProviderId = providerId;
            SourceRootId = sourceRootId;
            DayUtc = dayUtc;
            Model = model;
            Surface = surface;
            MachineId = machineId;
            AccountLabel = accountLabel;
        }

        public string ProviderId { get; }
        public string SourceRootId { get; }
        public DateTime DayUtc { get; }
        public string Model { get; }
        public string Surface { get; }
        public string? MachineId { get; }
        public string? AccountLabel { get; }
        public long InputTokens { get; set; }
        public long CachedInputTokens { get; set; }
        public long OutputTokens { get; set; }
        public long ReasoningTokens { get; set; }
        public long TotalTokens { get; set; }
    }

    private static IReadOnlyList<UsageEventRecord> ParseCodexFile(
        SourceRootRecord root,
        string filePath,
        UsageTelemetryQuickReportOptions options,
        CancellationToken cancellationToken) {
        var aggregates = new Dictionary<string, UsageAggregateBucket>(StringComparer.OrdinalIgnoreCase);
        var currentModel = default(string);
        CodexNormalizedUsage? previousTotals = null;
        CodexNormalizedUsage? previousLastUsage = null;

        foreach (var rawLine in ReadLinesShared(filePath)) {
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
                currentModel = ExtractCodexModel(payload) ?? currentModel;
                continue;
            }
            if (string.Equals(type, "turn_context", StringComparison.OrdinalIgnoreCase)) {
                currentModel = ExtractCodexModel(payload) ?? currentModel;
                continue;
            }
            if (!string.Equals(type, "event_msg", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(payload?.GetString("type"), "token_count", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var info = payload?.GetObject("info");
            var lastUsage = NormalizeCodexUsage(info?.GetObject("last_token_usage") ?? info?.GetObject("lastTokenUsage"));
            var totalUsage = NormalizeCodexUsage(info?.GetObject("total_token_usage") ?? info?.GetObject("totalTokenUsage"));
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
                usage = SubtractCodexUsage(totalUsage, previousTotals);
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

            AddAggregate(
                aggregates,
                providerId: "codex",
                sourceRootId: root.Id,
                dayUtc: timestampUtc.UtcDateTime.Date,
                model: ExtractCodexModel(payload) ?? currentModel ?? "unknown-model",
                surface: "cli",
                machineId: NormalizeOptional(options.MachineId) ?? NormalizeOptional(root.MachineLabel),
                accountLabel: NormalizeOptional(root.AccountHint),
                inputTokens: usage.InputTokens,
                cachedInputTokens: usage.CachedInputTokens,
                outputTokens: usage.OutputTokens,
                reasoningTokens: usage.ReasoningTokens,
                totalTokens: usage.TotalTokens);
        }

        return FinalizeAggregates(aggregates, CodexAdapterId);
    }

    private static IReadOnlyList<UsageEventRecord> ParseClaudeFile(
        SourceRootRecord root,
        string filePath,
        UsageTelemetryQuickReportOptions options,
        CancellationToken cancellationToken) {
        var candidates = new Dictionary<string, ClaudeUsageCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in ReadLinesShared(filePath)) {
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

            if (!string.Equals(entry.GetString("type"), "assistant", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var message = entry.GetObject("message");
            var usage = NormalizeClaudeUsage(message?.GetObject("usage"));
            if (usage is null || usage.TotalTokens <= 0) {
                continue;
            }
            if (!TryParseTimestampUtc(entry.GetString("timestamp"), out var timestampUtc)) {
                continue;
            }

            var key = BuildClaudeCandidateKey(
                NormalizeOptional(message?.GetString("id")),
                NormalizeOptional(entry.GetString("requestId") ?? entry.GetString("request_id")),
                timestampUtc);
            var candidate = new ClaudeUsageCandidate(
                key,
                timestampUtc,
                NormalizeOptional(message?.GetString("model")) ?? "unknown-model",
                usage);

            if (candidates.TryGetValue(key, out var existing) && !ShouldReplaceClaudeCandidate(existing, candidate)) {
                continue;
            }

            candidates[key] = candidate;
        }

        var aggregates = new Dictionary<string, UsageAggregateBucket>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates.Values.OrderBy(static value => value.TimestampUtc)) {
            AddAggregate(
                aggregates,
                providerId: "claude",
                sourceRootId: root.Id,
                dayUtc: candidate.TimestampUtc.UtcDateTime.Date,
                model: candidate.Model,
                surface: "cli",
                machineId: NormalizeOptional(options.MachineId) ?? NormalizeOptional(root.MachineLabel),
                accountLabel: NormalizeOptional(root.AccountHint),
                inputTokens: candidate.Usage.InputTokens,
                cachedInputTokens: candidate.Usage.CachedInputTokens,
                outputTokens: candidate.Usage.OutputTokens,
                reasoningTokens: candidate.Usage.ReasoningTokens,
                totalTokens: candidate.Usage.TotalTokens);
        }

        return FinalizeAggregates(aggregates, ClaudeAdapterId);
    }

    private static IEnumerable<string> EnumerateCodexCandidateFiles(string rootPath, bool preferRecentArtifacts) {
        if (File.Exists(rootPath)) {
            if (string.Equals(Path.GetExtension(rootPath), ".jsonl", StringComparison.OrdinalIgnoreCase)) {
                yield return Path.GetFullPath(rootPath);
            }
            yield break;
        }

        foreach (var directory in EnumerateCodexCandidateDirectories(Path.GetFullPath(rootPath))) {
            foreach (var file in EnumerateCodexFilesFromDirectory(directory, preferRecentArtifacts)) {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumerateCodexFilesFromDirectory(string directory, bool preferRecentArtifacts) {
        foreach (var file in OrderPathsByDirection(
                     EnumerateFilesSafe(directory, "*.jsonl", SearchOption.TopDirectoryOnly)
                         .Where(IsCodexSessionFile)
                         .Select(Path.GetFullPath),
                     preferRecentArtifacts,
                     GetLastWriteTimeUtcSafe)) {
            yield return file;
        }

        var partitionRoots = OrderPathsByDirection(
                EnumerateDirectoriesSafe(directory)
                    .Where(static value => IsIntegerPathSegment(Path.GetFileName(value), 4)),
                preferRecentArtifacts,
                GetNumericPathSegment)
            .ToArray();
        if (partitionRoots.Length == 0) {
            foreach (var file in OrderPathsByDirection(
                         EnumerateFilesSafe(directory, "*.jsonl", SearchOption.AllDirectories)
                             .Where(IsCodexSessionFile)
                             .Select(Path.GetFullPath),
                         preferRecentArtifacts,
                         GetLastWriteTimeUtcSafe)) {
                yield return file;
            }

            yield break;
        }

        foreach (var yearDirectory in partitionRoots) {
            var monthDirectories = OrderPathsByDirection(
                    EnumerateDirectoriesSafe(yearDirectory)
                        .Where(static value => IsIntegerPathSegment(Path.GetFileName(value), 2)),
                    preferRecentArtifacts,
                    GetNumericPathSegment)
                .ToArray();

            if (monthDirectories.Length == 0) {
                foreach (var file in OrderPathsByDirection(
                             EnumerateFilesSafe(yearDirectory, "*.jsonl", SearchOption.AllDirectories)
                                 .Where(IsCodexSessionFile)
                                 .Select(Path.GetFullPath),
                             preferRecentArtifacts,
                             GetLastWriteTimeUtcSafe)) {
                    yield return file;
                }

                continue;
            }

            foreach (var monthDirectory in monthDirectories) {
                var dayDirectories = OrderPathsByDirection(
                        EnumerateDirectoriesSafe(monthDirectory)
                            .Where(static value => IsIntegerPathSegment(Path.GetFileName(value), 2)),
                        preferRecentArtifacts,
                        GetNumericPathSegment)
                    .ToArray();

                if (dayDirectories.Length == 0) {
                    foreach (var file in OrderPathsByDirection(
                                 EnumerateFilesSafe(monthDirectory, "*.jsonl", SearchOption.AllDirectories)
                                     .Where(IsCodexSessionFile)
                                     .Select(Path.GetFullPath),
                                 preferRecentArtifacts,
                                 GetLastWriteTimeUtcSafe)) {
                        yield return file;
                    }

                    continue;
                }

                foreach (var dayDirectory in dayDirectories) {
                    foreach (var file in OrderPathsByDirection(
                                 EnumerateFilesSafe(dayDirectory, "*.jsonl", SearchOption.TopDirectoryOnly)
                                     .Where(IsCodexSessionFile)
                                     .Select(Path.GetFullPath),
                                 preferRecentArtifacts,
                                 GetLastWriteTimeUtcSafe)) {
                        yield return file;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateCodexCandidateDirectories(string rootPath) {
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
        if (Directory.Exists(sessionsPath)) {
            yield return sessionsPath;
        }
        var archivedPath = Path.Combine(rootPath, "archived_sessions");
        if (Directory.Exists(archivedPath)) {
            yield return archivedPath;
        }
        if (!Directory.Exists(sessionsPath) && !Directory.Exists(archivedPath)) {
            yield return rootPath;
        }
    }

    private static IEnumerable<string> EnumerateClaudeCandidateFiles(string rootPath, bool preferRecentArtifacts) {
        if (File.Exists(rootPath)) {
            if (string.Equals(Path.GetExtension(rootPath), ".jsonl", StringComparison.OrdinalIgnoreCase)) {
                yield return Path.GetFullPath(rootPath);
            }
            yield break;
        }

        foreach (var directory in EnumerateClaudeCandidateDirectories(rootPath)) {
            var files = Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .ToArray();
            var orderedFiles = preferRecentArtifacts
                ? files.OrderByDescending(GetLastWriteTimeUtcSafe).ThenBy(static value => value, StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase);
            foreach (var file in orderedFiles) {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumerateClaudeCandidateDirectories(string rootPath) {
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
        yield return Directory.Exists(projectsPath) ? projectsPath : normalizedRoot;
    }

    private static IEnumerable<string> ReadLinesShared(string filePath) {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        while (true) {
            var line = reader.ReadLine();
            if (line is null) {
                yield break;
            }

            yield return line;
        }
    }

    private static bool TryParseTimestampUtc(string? value, out DateTimeOffset timestampUtc) {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestampUtc)) {
            timestampUtc = timestampUtc.ToUniversalTime();
            return true;
        }

        timestampUtc = default;
        return false;
    }

    private static string? ExtractCodexModel(JsonObject? payload) {
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

        return NormalizeOptional(payload.GetObject("metadata")?.GetString("model"));
    }

    private static IEnumerable<string> EnumerateFilesSafe(string directory, string searchPattern, SearchOption searchOption) {
        try {
            return Directory.EnumerateFiles(directory, searchPattern, searchOption);
        } catch {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string directory) {
        try {
            return Directory.EnumerateDirectories(directory);
        } catch {
            return Array.Empty<string>();
        }
    }

    private static bool IsIntegerPathSegment(string? value, int expectedLength) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var normalized = value ?? string.Empty;
        if (normalized.Length != expectedLength) {
            return false;
        }

        for (var i = 0; i < normalized.Length; i++) {
            if (!char.IsDigit(normalized[i])) {
                return false;
            }
        }

        return true;
    }

    private static long GetNumericPathSegment(string path) {
        return long.TryParse(
            Path.GetFileName(path),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : long.MinValue;
    }

    private static IEnumerable<string> OrderPathsByDirection<TOrder>(
        IEnumerable<string> source,
        bool descending,
        Func<string, TOrder> orderSelector) where TOrder : IComparable<TOrder> {
        return descending
            ? source.OrderByDescending(orderSelector).ThenBy(static value => value, StringComparer.OrdinalIgnoreCase)
            : source.OrderBy(orderSelector).ThenBy(static value => value, StringComparer.OrdinalIgnoreCase);
    }

    private static CodexNormalizedUsage? NormalizeCodexUsage(JsonObject? obj) {
        if (obj is null) {
            return null;
        }

        var inputTokens = ReadInt64(obj, "input_tokens", "inputTokens") ?? 0;
        var cachedInputTokens = ReadInt64(obj, "cached_input_tokens", "cachedInputTokens", "cache_read_input_tokens", "cacheReadInputTokens") ?? 0;
        var outputTokens = ReadInt64(obj, "output_tokens", "outputTokens") ?? 0;
        var reasoningTokens = ReadInt64(obj, "reasoning_output_tokens", "reasoningOutputTokens", "reasoning_tokens", "reasoningTokens") ?? 0;
        var totalTokens = ReadInt64(obj, "total_tokens", "totalTokens") ?? Math.Max(0, inputTokens + outputTokens);
        return new CodexNormalizedUsage(Math.Max(0, inputTokens), Math.Max(0, cachedInputTokens), Math.Max(0, outputTokens), Math.Max(0, reasoningTokens), Math.Max(0, totalTokens));
    }

    private static CodexNormalizedUsage SubtractCodexUsage(CodexNormalizedUsage current, CodexNormalizedUsage? previous) {
        return new CodexNormalizedUsage(
            Math.Max(0, current.InputTokens - (previous?.InputTokens ?? 0)),
            Math.Max(0, current.CachedInputTokens - (previous?.CachedInputTokens ?? 0)),
            Math.Max(0, current.OutputTokens - (previous?.OutputTokens ?? 0)),
            Math.Max(0, current.ReasoningTokens - (previous?.ReasoningTokens ?? 0)),
            Math.Max(0, current.TotalTokens - (previous?.TotalTokens ?? 0)));
    }

    private static ClaudeNormalizedUsage? NormalizeClaudeUsage(JsonObject? usage) {
        if (usage is null) {
            return null;
        }

        var inputTokens = ReadInt64(usage, "input_tokens", "inputTokens") ?? 0;
        var cacheCreationTokens = ReadInt64(usage, "cache_creation_input_tokens", "cacheCreationInputTokens") ?? 0;
        var cacheReadTokens = ReadInt64(usage, "cache_read_input_tokens", "cacheReadInputTokens") ?? 0;
        var outputTokens = ReadInt64(usage, "output_tokens", "outputTokens") ?? 0;
        var reasoningTokens = ReadInt64(usage, "reasoning_tokens", "reasoningTokens")
                              ?? ReadInt64(usage.GetObject("output_tokens_details"), "reasoning_tokens", "reasoningTokens")
                              ?? 0;
        var totalTokens = ReadInt64(usage, "total_tokens", "totalTokens")
                          ?? Math.Max(0, inputTokens + cacheCreationTokens + cacheReadTokens + outputTokens);
        return new ClaudeNormalizedUsage(
            Math.Max(0, inputTokens + cacheCreationTokens),
            Math.Max(0, cacheReadTokens),
            Math.Max(0, outputTokens),
            Math.Max(0, reasoningTokens),
            Math.Max(0, totalTokens));
    }

    private static long? ReadInt64(JsonObject? obj, params string[] keys) {
        if (obj is null || keys is null || keys.Length == 0) {
            return null;
        }

        foreach (var key in keys) {
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

    private static string BuildClaudeCandidateKey(string? messageId, string? requestId, DateTimeOffset timestampUtc) {
        if (!string.IsNullOrWhiteSpace(messageId) || !string.IsNullOrWhiteSpace(requestId)) {
            return (NormalizeOptional(messageId) ?? "no-message") + "|" + (NormalizeOptional(requestId) ?? "no-request");
        }

        return timestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static bool ShouldReplaceClaudeCandidate(ClaudeUsageCandidate existing, ClaudeUsageCandidate candidate) {
        if (candidate.Usage.TotalTokens != existing.Usage.TotalTokens) {
            return candidate.Usage.TotalTokens > existing.Usage.TotalTokens;
        }

        return candidate.TimestampUtc >= existing.TimestampUtc;
    }

    private static bool IsCodexSessionFile(string path) {
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

    private sealed record CodexNormalizedUsage(long InputTokens, long CachedInputTokens, long OutputTokens, long ReasoningTokens, long TotalTokens);
    private sealed record ClaudeNormalizedUsage(long InputTokens, long CachedInputTokens, long OutputTokens, long ReasoningTokens, long TotalTokens);
    private sealed record ClaudeUsageCandidate(string Key, DateTimeOffset TimestampUtc, string Model, ClaudeNormalizedUsage Usage);
}
