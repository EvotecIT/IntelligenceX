using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Telemetry.Usage.Claude;
using IntelligenceX.Telemetry.Usage.Codex;
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
    private static readonly IReadOnlyList<UsageTelemetryQuickReportProviderDefinition> QuickReportProviders = new[] {
        new UsageTelemetryQuickReportProviderDefinition(
            "codex",
            "codex.quick-report",
            "codex.quick-report/v1",
            CodexQuickReportImport.EnumerateCandidateFiles,
            static (root, filePath, options, cancellationToken, definition) =>
                CodexQuickReportImport.ParseFile(root, filePath, options, definition.AdapterId, definition.ProviderId, cancellationToken)),
        new UsageTelemetryQuickReportProviderDefinition(
            "claude",
            "claude.quick-report",
            "claude.quick-report/v1",
            ClaudeQuickReportImport.EnumerateCandidateFiles,
            static (root, filePath, options, cancellationToken, definition) =>
                ClaudeQuickReportImport.ParseFile(root, filePath, options, definition.AdapterId, definition.ProviderId, cancellationToken)),
        new UsageTelemetryQuickReportProviderDefinition(
            "lmstudio",
            "lmstudio.quick-report",
            "lmstudio.quick-report/v1",
            LmStudio.LmStudioQuickReportImport.EnumerateCandidateFiles,
            static (root, filePath, options, cancellationToken, definition) =>
                LmStudio.LmStudioQuickReportImport.ParseFile(root, filePath, options, definition.AdapterId, definition.ProviderId, cancellationToken))
    };

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

            if (TryResolveQuickReportProvider(root.ProviderId, out var quickReportProvider)) {
                ScanRoot(root, quickReportProvider, effectiveOptions, allRecords, ref parsedArtifacts, result, cancellationToken);
            }

            if (result.ArtifactBudgetReached) {
                break;
            }
        }

        result.Events.AddRange(MergeRecords(allRecords));
        return Task.FromResult(result);
    }

    private static void ScanRoot(
        SourceRootRecord root,
        UsageTelemetryQuickReportProviderDefinition provider,
        UsageTelemetryQuickReportOptions options,
        List<UsageEventRecord> allRecords,
        ref int parsedArtifacts,
        UsageTelemetryQuickReportResult result,
        CancellationToken cancellationToken) {
        foreach (var filePath in provider.EnumerateCandidateFiles(root.Path, options.PreferRecentArtifacts)) {
            cancellationToken.ThrowIfCancellationRequested();
            var artifact = RawArtifactDescriptor.CreateFile(root.Id, provider.AdapterId, filePath, provider.ParserVersion, options.UtcNow());
            if (TryReuseCachedQuickRecords(options, artifact, out var cachedRecords)) {
                result.ArtifactsReused++;
                allRecords.AddRange(cachedRecords);
                continue;
            }
            if (!TryReserveArtifactBudget(options, ref parsedArtifacts, result)) {
                break;
            }

            var records = provider.ParseFile(root, filePath, options, cancellationToken, provider);
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
        var canonicalFilter = UsageTelemetryProviderCatalog.ResolveCanonicalProviderId(filter);
        if (canonicalFilter is null) {
            return true;
        }

        return string.Equals(
            UsageTelemetryProviderCatalog.ResolveCanonicalProviderId(providerId),
            canonicalFilter,
            StringComparison.OrdinalIgnoreCase);
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

    private static bool TryResolveQuickReportProvider(
        string? providerId,
        out UsageTelemetryQuickReportProviderDefinition definition) {
        var canonicalProviderId = UsageTelemetryProviderCatalog.ResolveCanonicalProviderId(providerId);
        var resolvedDefinition = QuickReportProviders
            .FirstOrDefault(candidate =>
                string.Equals(candidate.ProviderId, canonicalProviderId, StringComparison.OrdinalIgnoreCase));
        if (resolvedDefinition is not null) {
            definition = resolvedDefinition;
            return true;
        }

        definition = null!;
        return false;
    }

    private sealed record UsageTelemetryQuickReportProviderDefinition(
        string ProviderId,
        string AdapterId,
        string ParserVersion,
        Func<string, bool, IEnumerable<string>> EnumerateCandidateFiles,
        Func<SourceRootRecord, string, UsageTelemetryQuickReportOptions, CancellationToken, UsageTelemetryQuickReportProviderDefinition, IReadOnlyList<UsageEventRecord>> ParseFile);
}
