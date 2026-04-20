using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Shared snapshot service for lightweight usage discovery and aggregation.
/// </summary>
public sealed class UsageTelemetrySnapshotService {
    private const int TrayMaxArtifacts = 60;
    private const int StartupCachedArtifactsPerSourceRoot = 24;
    private readonly Func<IReadOnlyList<IUsageTelemetryRootDiscovery>> _createDiscoveries;
    private readonly Func<UsageTelemetryQuickReportScanner> _createScanner;
    private readonly IRawArtifactStore? _rawArtifactStore;

    /// <summary>
    /// Initializes a new shared snapshot service.
    /// </summary>
    public UsageTelemetrySnapshotService(IRawArtifactStore? rawArtifactStore = null)
        : this(
            () => UsageTelemetryProviderCatalog.CreateRootDiscoveries(),
            static () => new UsageTelemetryQuickReportScanner(),
            rawArtifactStore) {
    }

    internal UsageTelemetrySnapshotService(
        Func<IReadOnlyList<IUsageTelemetryRootDiscovery>> createDiscoveries,
        Func<UsageTelemetryQuickReportScanner> createScanner,
        IRawArtifactStore? rawArtifactStore = null) {
        _createDiscoveries = createDiscoveries ?? throw new ArgumentNullException(nameof(createDiscoveries));
        _createScanner = createScanner ?? throw new ArgumentNullException(nameof(createScanner));
        _rawArtifactStore = rawArtifactStore;
    }

    /// <summary>
    /// Attempts to load a quick startup snapshot directly from cached quick-report artifacts.
    /// </summary>
    /// <returns>A cached snapshot when quick-report artifact state is available; otherwise <see langword="null"/>.</returns>
    public UsageTelemetrySnapshot? TryLoadCachedSnapshot() {
        if (_rawArtifactStore is null) {
            return null;
        }

        var allRoots = DiscoverAllRoots();
        var enabledRoots = allRoots
            .Where(static root => root.Enabled)
            .ToArray();

        var artifacts = _rawArtifactStore.GetRecentPerSourceRoot(StartupCachedArtifactsPerSourceRoot);

        if (artifacts.Count == 0) {
            return null;
        }

        var rawEvents = UsageTelemetryQuickReportScanner.RestoreRawFromCachedArtifacts(artifacts);
        var events = UsageTelemetryQuickReportScanner.RestoreFromCachedArtifacts(artifacts);
        if (events.Count == 0) {
            return null;
        }

        var scannedAtUtc = artifacts
            .Select(static artifact => artifact.ImportedAtUtc)
            .Where(static value => value > DateTimeOffset.MinValue)
            .DefaultIfEmpty(DateTimeOffset.UtcNow)
            .Max();
        return new UsageTelemetrySnapshot(
            events,
            scannedAtUtc,
            rootsFound: allRoots.Count,
            scanDurationMs: 0,
            errors: Array.Empty<string>(),
            discoveredProviderIds: events
                .Select(static item => item.ProviderId)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Concat(enabledRoots.Select(static root => root.ProviderId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            sourceRoots: enabledRoots,
            health: BuildCachedSnapshotHealth(allRoots, enabledRoots, artifacts, events),
            rawEvents: rawEvents);
    }

    /// <summary>
    /// Discovers usage roots and returns a provider-grouped quick snapshot.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <param name="progress">Optional progress receiver for staged scan updates.</param>
    /// <param name="startupWarmup">When true, returns a capped recent-artifact startup snapshot before the full scan catches up.</param>
    /// <returns>Usage snapshot with events, timing, and any non-fatal discovery errors.</returns>
    public async Task<UsageTelemetrySnapshot> ScanAsync(
        CancellationToken cancellationToken = default,
        IProgress<UsageTelemetryScanProgress>? progress = null,
        bool startupWarmup = false) {
        progress?.Report(new UsageTelemetryScanProgress(
            "Discovering local usage sources...",
            "Checking local Codex, Claude, Copilot, and LM Studio roots."));
        var discoveredRoots = DiscoverAllRootsWithErrors();
        var allRoots = discoveredRoots.Roots;
        var errors = discoveredRoots.Errors.ToList();

        if (allRoots.Count == 0) {
            return new UsageTelemetrySnapshot(
                Array.Empty<UsageEventRecord>(),
                DateTimeOffset.UtcNow,
                0,
                0,
                errors,
                discoveredProviderIds: Array.Empty<string>(),
                sourceRoots: Array.Empty<SourceRootRecord>());
        }

        var rootsByProvider = allRoots
            .Where(static root => root.Enabled)
            .GroupBy(static root => root.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        progress?.Report(new UsageTelemetryScanProgress(
            "Preparing provider scan...",
            rootsByProvider.Length == 0
                ? "No enabled provider roots were found."
                : "Found " + rootsByProvider.Length + " provider groups to scan.",
            discoveredProviderIds: rootsByProvider.Select(static group => group.Key).ToArray(),
            completedProviderCount: 0,
            totalProviderCount: rootsByProvider.Length));

        var allEvents = new List<UsageEventRecord>();
        var providerResults = new List<ProviderScanResult>();
        var stopwatch = Stopwatch.StartNew();

        var providerTasks = rootsByProvider
            .Select(providerRoots => ScanProviderAsync(providerRoots, cancellationToken, startupWarmup, progress))
            .ToList();
        var completedProviders = 0;
        while (providerTasks.Count > 0) {
            cancellationToken.ThrowIfCancellationRequested();
            var completedTask = await Task.WhenAny(providerTasks).ConfigureAwait(false);
            providerTasks.Remove(completedTask);

            var result = await completedTask.ConfigureAwait(false);
            providerResults.Add(result);
            completedProviders++;
            var providerTitle = UsageTelemetryProviderCatalog.ResolveDisplayTitle(result.ProviderId);
            if (result.ErrorMessage is null) {
                allEvents.AddRange(result.Events);
                progress?.Report(new UsageTelemetryScanProgress(
                    "Loaded " + providerTitle + ".",
                    BuildProviderScanSummary(result.ScannerResult),
                    completedProvider: new UsageTelemetryProviderScanResult(result.ProviderId, result.Events),
                    completedProviderCount: completedProviders,
                    totalProviderCount: rootsByProvider.Length));
                continue;
            }

            errors.Add(result.ProviderId + ": scan failed - " + result.ErrorMessage);
            progress?.Report(new UsageTelemetryScanProgress(
                "Scan issue in " + providerTitle + ".",
                result.ErrorMessage,
                completedProviderCount: completedProviders,
                totalProviderCount: rootsByProvider.Length));
        }

        stopwatch.Stop();

        return new UsageTelemetrySnapshot(
            allEvents,
            DateTimeOffset.UtcNow,
            allRoots.Count,
            stopwatch.ElapsedMilliseconds,
            errors,
            discoveredProviderIds: rootsByProvider.Select(static group => group.Key).ToArray(),
            sourceRoots: rootsByProvider.SelectMany(static group => group).ToArray(),
            health: BuildLiveSnapshotHealth(
                rootsByProvider.SelectMany(static group => group).ToArray(),
                allEvents,
                providerResults,
                errors,
                rootsByProvider.Select(static group => group.Key).ToArray()),
            rawEvents: providerResults
                .SelectMany(static result => result.RawEvents)
                .OrderBy(static usageEvent => usageEvent.TimestampUtc)
                .ToArray());
    }

    private IReadOnlyList<SourceRootRecord> DiscoverAllRoots() {
        return DiscoverAllRootsWithErrors().Roots;
    }

    private (IReadOnlyList<SourceRootRecord> Roots, IReadOnlyList<string> Errors) DiscoverAllRootsWithErrors() {
        var discoveries = _createDiscoveries();
        var allRoots = new List<SourceRootRecord>();
        var errors = new List<string>();

        foreach (var discovery in discoveries) {
            try {
                allRoots.AddRange(discovery.DiscoverRoots());
            } catch (Exception ex) {
                errors.Add(discovery.ProviderId + ": " + ex.Message);
            }
        }

        return (allRoots, errors);
    }

    private static string BuildProviderScanSummary(UsageTelemetryQuickReportResult result) {
        var parts = new List<string>();
        if (result.Events.Count > 0) {
            parts.Add(result.Events.Count.ToString() + " events");
        }

        if (result.ArtifactsReused > 0) {
            parts.Add(result.ArtifactsReused.ToString() + " cached");
        }

        if (result.ArtifactsParsed > 0) {
            parts.Add(result.ArtifactsParsed.ToString() + " parsed");
        }

        if (result.DuplicateRecordsCollapsed > 0) {
            parts.Add(result.DuplicateRecordsCollapsed.ToString() + " deduped");
        }

        if (parts.Count == 0) {
            return "No usage events found for this provider.";
        }

        return string.Join(" • ", parts);
    }

    private async Task<ProviderScanResult> ScanProviderAsync(
        IGrouping<string, SourceRootRecord> providerRoots,
        CancellationToken cancellationToken,
        bool startupWarmup,
        IProgress<UsageTelemetryScanProgress>? progress) {
        var scanner = _createScanner();
        var providerTitle = UsageTelemetryProviderCatalog.ResolveDisplayTitle(providerRoots.Key);
        var options = new UsageTelemetryQuickReportOptions {
            PreferRecentArtifacts = true,
            RawArtifactStore = _rawArtifactStore,
            MaxArtifacts = startupWarmup || _rawArtifactStore is null
                ? TrayMaxArtifacts
                : null,
            Progress = update => {
                if (progress is null || update.ArtifactOrdinal is null || update.ArtifactCount is null) {
                    return;
                }

                var providerArtifactProgress = new UsageTelemetryProviderArtifactProgress(
                    providerRoots.Key,
                    update.Phase,
                    update.ArtifactOrdinal.Value,
                    update.ArtifactCount.Value,
                    update.ArtifactSizeBytes,
                    update.ParsedArtifacts ?? 0,
                    update.ReusedArtifacts ?? 0,
                    update.ArtifactPath,
                    update.RootPath,
                    update.AdapterId);
                progress.Report(new UsageTelemetryScanProgress(
                    "Scanning " + providerTitle + " "
                    + providerArtifactProgress.ArtifactOrdinal.ToString() + "/"
                    + providerArtifactProgress.ArtifactCount.ToString() + " files...",
                    BuildProviderArtifactProgressDetail(providerArtifactProgress),
                    providerArtifactProgress: providerArtifactProgress));
            }
        };

        try {
            var result = await scanner
                .ScanAsync(providerRoots.ToArray(), options, cancellationToken)
                .ConfigureAwait(false);
            return new ProviderScanResult(providerRoots.Key, result.Events.ToList(), result.RawEvents.ToList(), result, null);
        } catch (Exception ex) {
            return new ProviderScanResult(
                providerRoots.Key,
                Array.Empty<UsageEventRecord>(),
                Array.Empty<UsageEventRecord>(),
                new UsageTelemetryQuickReportResult(),
                ex.Message);
        }
    }

    private sealed record ProviderScanResult(
        string ProviderId,
        IReadOnlyList<UsageEventRecord> Events,
        IReadOnlyList<UsageEventRecord> RawEvents,
        UsageTelemetryQuickReportResult ScannerResult,
        string? ErrorMessage);

    private static string BuildProviderArtifactProgressDetail(UsageTelemetryProviderArtifactProgress providerArtifactProgress) {
        var parts = new List<string>();
        var completedArtifacts = providerArtifactProgress.ParsedArtifacts + providerArtifactProgress.ReusedArtifacts;
        if (providerArtifactProgress.ArtifactCount > 0) {
            parts.Add(completedArtifacts.ToString() + "/" + providerArtifactProgress.ArtifactCount.ToString() + " completed");
        }

        var phaseLabel = providerArtifactProgress.Phase switch {
            "artifact-start" => "checking",
            "artifact-cache" => "cached",
            "artifact" => "parsed",
            _ => "scanning"
        };
        parts.Add(phaseLabel);

        if (!string.IsNullOrWhiteSpace(providerArtifactProgress.ArtifactPath)) {
            parts.Add(Path.GetFileName(providerArtifactProgress.ArtifactPath));
        }

        if (providerArtifactProgress.ArtifactSizeBytes is > 0) {
            parts.Add(FormatArtifactSize(providerArtifactProgress.ArtifactSizeBytes.Value));
        }

        if (providerArtifactProgress.ParsedArtifacts > 0) {
            parts.Add(providerArtifactProgress.ParsedArtifacts.ToString() + " parsed");
        }

        if (providerArtifactProgress.ReusedArtifacts > 0) {
            parts.Add(providerArtifactProgress.ReusedArtifacts.ToString() + " cached");
        }

        return parts.Count == 0 ? "Scanning usage artifacts." : string.Join(" • ", parts);
    }

    private static string FormatArtifactSize(long sizeBytes) {
        var size = Math.Max(0L, sizeBytes);
        string[] units = ["B", "KB", "MB", "GB"];
        double scaled = size;
        var unitIndex = 0;
        while (scaled >= 1024d && unitIndex < units.Length - 1) {
            scaled /= 1024d;
            unitIndex++;
        }

        return scaled >= 10d || unitIndex == 0
            ? scaled.ToString("0", CultureInfo.InvariantCulture) + units[unitIndex]
            : scaled.ToString("0.0", CultureInfo.InvariantCulture) + units[unitIndex];
    }

    private static UsageTelemetrySnapshotHealth BuildCachedSnapshotHealth(
        IReadOnlyList<SourceRootRecord> allRoots,
        IReadOnlyList<SourceRootRecord> enabledRoots,
        IReadOnlyList<RawArtifactDescriptor> artifacts,
        IReadOnlyList<UsageEventRecord> events) {
        var providerIds = enabledRoots
            .Select(static root => root.ProviderId)
            .Concat(events.Select(static item => item.ProviderId))
            .Where(static providerId => !string.IsNullOrWhiteSpace(providerId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rootLookup = enabledRoots.ToDictionary(static root => root.Id, StringComparer.OrdinalIgnoreCase);
        var providerHealth = new List<UsageTelemetryProviderHealth>(providerIds.Length);
        foreach (var providerId in providerIds) {
            var providerEvents = events
                .Where(item => string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var providerRoots = enabledRoots
                .Where(root => string.Equals(root.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var reusedArtifacts = artifacts.Count(artifact =>
                rootLookup.TryGetValue(artifact.SourceRootId, out var root)
                && string.Equals(root.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
            providerHealth.Add(new UsageTelemetryProviderHealth(
                providerId,
                providerRoots.Length,
                BuildDistinctAccountLabels(providerEvents, providerRoots),
                providerEvents.Length,
                parsedArtifacts: 0,
                reusedArtifacts,
                duplicateRecordsCollapsed: 0,
                latestEventUtc: GetLatestEventTimestampUtc(providerEvents),
                isPartialScan: false));
        }

        return new UsageTelemetrySnapshotHealth(
            isCachedSnapshot: true,
            isPartialScan: false,
            providerCount: providerIds.Length,
            rootsCount: enabledRoots.Count,
            accountLabels: BuildDistinctAccountLabels(events, enabledRoots),
            eventsCount: events.Count,
            parsedArtifacts: 0,
            reusedArtifacts: artifacts.Count,
            duplicateRecordsCollapsed: 0,
            latestEventUtc: GetLatestEventTimestampUtc(events),
            issueCount: 0,
            providerHealth: providerHealth);
    }

    private static UsageTelemetrySnapshotHealth BuildLiveSnapshotHealth(
        IReadOnlyList<SourceRootRecord> enabledRoots,
        IReadOnlyList<UsageEventRecord> events,
        IReadOnlyList<ProviderScanResult> providerResults,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> discoveredProviderIds) {
        var providerIds = discoveredProviderIds
            .Concat(enabledRoots.Select(static root => root.ProviderId))
            .Concat(events.Select(static item => item.ProviderId))
            .Where(static providerId => !string.IsNullOrWhiteSpace(providerId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var providerHealth = new List<UsageTelemetryProviderHealth>(providerIds.Length);
        foreach (var providerId in providerIds) {
            var result = providerResults.FirstOrDefault(item =>
                string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
            var diagnostics = result?.ScannerResult.ProviderDiagnostics.FirstOrDefault(item =>
                string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
            var providerEvents = events
                .Where(item => string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var providerRoots = enabledRoots
                .Where(root => string.Equals(root.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            providerHealth.Add(new UsageTelemetryProviderHealth(
                providerId,
                diagnostics?.RootsConsidered ?? providerRoots.Length,
                BuildDistinctAccountLabels(providerEvents, providerRoots),
                providerEvents.Length,
                diagnostics?.ArtifactsParsed ?? result?.ScannerResult.ArtifactsParsed ?? 0,
                diagnostics?.ArtifactsReused ?? result?.ScannerResult.ArtifactsReused ?? 0,
                diagnostics?.DuplicateRecordsCollapsed ?? result?.ScannerResult.DuplicateRecordsCollapsed ?? 0,
                latestEventUtc: GetLatestEventTimestampUtc(providerEvents),
                isPartialScan: result?.ScannerResult.ArtifactBudgetReached ?? false));
        }

        return new UsageTelemetrySnapshotHealth(
            isCachedSnapshot: false,
            isPartialScan: providerResults.Any(static item => item.ScannerResult.ArtifactBudgetReached),
            providerCount: providerIds.Length,
            rootsCount: enabledRoots.Count,
            accountLabels: BuildDistinctAccountLabels(events, enabledRoots),
            eventsCount: events.Count,
            parsedArtifacts: providerResults.Sum(static item => item.ScannerResult.ArtifactsParsed),
            reusedArtifacts: providerResults.Sum(static item => item.ScannerResult.ArtifactsReused),
            duplicateRecordsCollapsed: providerResults.Sum(static item => item.ScannerResult.DuplicateRecordsCollapsed),
            latestEventUtc: GetLatestEventTimestampUtc(events),
            issueCount: errors.Count,
            providerHealth: providerHealth);
    }

    private static IReadOnlyList<string> BuildDistinctAccountLabels(
        IEnumerable<UsageEventRecord> events,
        IEnumerable<SourceRootRecord> roots) {
        return events
            .SelectMany(static item => new[] { item.AccountLabel, item.ProviderAccountId })
            .Concat(roots.Select(static root => root.AccountHint))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DateTimeOffset? GetLatestEventTimestampUtc(IEnumerable<UsageEventRecord> events) {
        var latest = DateTimeOffset.MinValue;
        foreach (var usageEvent in events) {
            if (usageEvent.TimestampUtc > latest) {
                latest = usageEvent.TimestampUtc;
            }
        }

        return latest == DateTimeOffset.MinValue ? null : latest;
    }
}

/// <summary>
/// Progress update emitted while a tray usage snapshot is being built.
/// </summary>
public sealed class UsageTelemetryScanProgress {
    /// <summary>
    /// Initializes a scan progress payload.
    /// </summary>
    public UsageTelemetryScanProgress(
        string statusText,
        string? detailText = null,
        IReadOnlyList<string>? discoveredProviderIds = null,
        UsageTelemetryProviderScanResult? completedProvider = null,
        UsageTelemetryProviderArtifactProgress? providerArtifactProgress = null,
        int completedProviderCount = 0,
        int totalProviderCount = 0) {
        StatusText = string.IsNullOrWhiteSpace(statusText) ? "Scanning providers..." : statusText.Trim();
        var normalizedDetailText = detailText?.Trim();
        DetailText = string.IsNullOrWhiteSpace(normalizedDetailText)
            ? null
            : normalizedDetailText;
        DiscoveredProviderIds = discoveredProviderIds;
        CompletedProvider = completedProvider;
        ProviderArtifactProgress = providerArtifactProgress;
        CompletedProviderCount = Math.Max(0, completedProviderCount);
        TotalProviderCount = Math.Max(0, totalProviderCount);
    }

    /// <summary>
    /// Gets the primary status line.
    /// </summary>
    public string StatusText { get; }

    /// <summary>
    /// Gets the optional secondary detail line.
    /// </summary>
    public string? DetailText { get; }

    /// <summary>
    /// Gets the discovered provider ids when available.
    /// </summary>
    public IReadOnlyList<string>? DiscoveredProviderIds { get; }

    /// <summary>
    /// Gets the completed provider payload when one provider finishes scanning.
    /// </summary>
    public UsageTelemetryProviderScanResult? CompletedProvider { get; }

    /// <summary>
    /// Gets the in-flight artifact progress for the currently active provider when available.
    /// </summary>
    public UsageTelemetryProviderArtifactProgress? ProviderArtifactProgress { get; }

    /// <summary>
    /// Gets how many providers have completed scanning.
    /// </summary>
    public int CompletedProviderCount { get; }

    /// <summary>
    /// Gets the total providers expected in the current scan.
    /// </summary>
    public int TotalProviderCount { get; }
}

/// <summary>
/// Artifact-level progress for an in-flight provider scan.
/// </summary>
public sealed class UsageTelemetryProviderArtifactProgress {
    /// <summary>
    /// Initializes provider artifact progress.
    /// </summary>
    public UsageTelemetryProviderArtifactProgress(
        string providerId,
        string? phase,
        int artifactOrdinal,
        int artifactCount,
        long? artifactSizeBytes,
        int parsedArtifacts,
        int reusedArtifacts,
        string? artifactPath,
        string? rootPath,
        string? adapterId) {
        ProviderId = string.IsNullOrWhiteSpace(providerId) ? "unknown" : providerId.Trim();
        Phase = NormalizeOptional(phase) ?? "artifact";
        ArtifactOrdinal = Math.Max(0, artifactOrdinal);
        ArtifactCount = Math.Max(0, artifactCount);
        ArtifactSizeBytes = artifactSizeBytes.GetValueOrDefault() > 0 ? artifactSizeBytes : null;
        ParsedArtifacts = Math.Max(0, parsedArtifacts);
        ReusedArtifacts = Math.Max(0, reusedArtifacts);
        ArtifactPath = NormalizeOptional(artifactPath);
        RootPath = NormalizeOptional(rootPath);
        AdapterId = NormalizeOptional(adapterId);
    }

    /// <summary>
    /// Gets the provider id being scanned.
    /// </summary>
    public string ProviderId { get; }

    /// <summary>
    /// Gets the current artifact phase.
    /// </summary>
    public string Phase { get; }

    /// <summary>
    /// Gets the 1-based artifact position within the current root scan.
    /// </summary>
    public int ArtifactOrdinal { get; }

    /// <summary>
    /// Gets the total artifacts discovered for the current root scan.
    /// </summary>
    public int ArtifactCount { get; }

    /// <summary>
    /// Gets the current artifact size in bytes when known.
    /// </summary>
    public long? ArtifactSizeBytes { get; }

    /// <summary>
    /// Gets how many artifacts have been parsed from disk so far.
    /// </summary>
    public int ParsedArtifacts { get; }

    /// <summary>
    /// Gets how many artifacts have been satisfied from cached quick-report state so far.
    /// </summary>
    public int ReusedArtifacts { get; }

    /// <summary>
    /// Gets the current artifact path when known.
    /// </summary>
    public string? ArtifactPath { get; }

    /// <summary>
    /// Gets the root path currently being scanned when known.
    /// </summary>
    public string? RootPath { get; }

    /// <summary>
    /// Gets the active adapter id when known.
    /// </summary>
    public string? AdapterId { get; }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

/// <summary>
/// Completed usage payload for a single provider scan.
/// </summary>
public sealed class UsageTelemetryProviderScanResult {
    /// <summary>
    /// Initializes a provider scan payload.
    /// </summary>
    public UsageTelemetryProviderScanResult(string providerId, IReadOnlyList<UsageEventRecord> events) {
        ProviderId = string.IsNullOrWhiteSpace(providerId) ? "unknown" : providerId.Trim();
        Events = events ?? Array.Empty<UsageEventRecord>();
    }

    /// <summary>
    /// Gets the provider id that completed scanning.
    /// </summary>
    public string ProviderId { get; }

    /// <summary>
    /// Gets the scanned events for the provider.
    /// </summary>
    public IReadOnlyList<UsageEventRecord> Events { get; }
}

/// <summary>
/// Lightweight snapshot returned by <see cref="UsageTelemetrySnapshotService"/>.
/// </summary>
public sealed class UsageTelemetrySnapshot {
    /// <summary>
    /// Initializes a new snapshot.
    /// </summary>
    public UsageTelemetrySnapshot(
        IReadOnlyList<UsageEventRecord> events,
        DateTimeOffset scannedAtUtc,
        int rootsFound,
        long scanDurationMs,
        IReadOnlyList<string> errors,
        IReadOnlyList<string>? discoveredProviderIds = null,
        IReadOnlyList<SourceRootRecord>? sourceRoots = null,
        UsageTelemetrySnapshotHealth? health = null,
        IReadOnlyList<UsageEventRecord>? rawEvents = null) {
        Events = events ?? Array.Empty<UsageEventRecord>();
        RawEvents = rawEvents ?? Events;
        ScannedAtUtc = scannedAtUtc;
        RootsFound = Math.Max(0, rootsFound);
        ScanDurationMs = Math.Max(0L, scanDurationMs);
        Errors = errors ?? Array.Empty<string>();
        DiscoveredProviderIds = discoveredProviderIds ?? Array.Empty<string>();
        SourceRoots = sourceRoots ?? Array.Empty<SourceRootRecord>();
        Health = health;
    }

    /// <summary>
    /// Gets the discovered usage events.
    /// </summary>
    public IReadOnlyList<UsageEventRecord> Events { get; }

    /// <summary>
    /// Gets deduplicated per-turn usage events before display rollup merging.
    /// </summary>
    public IReadOnlyList<UsageEventRecord> RawEvents { get; }

    /// <summary>
    /// Gets the snapshot capture time.
    /// </summary>
    public DateTimeOffset ScannedAtUtc { get; }

    /// <summary>
    /// Gets the number of roots discovered before scanning.
    /// </summary>
    public int RootsFound { get; }

    /// <summary>
    /// Gets the scan duration in milliseconds.
    /// </summary>
    public long ScanDurationMs { get; }

    /// <summary>
    /// Gets non-fatal discovery or scan errors.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Gets the provider ids discovered during the scan, even when some providers produced no events.
    /// </summary>
    public IReadOnlyList<string> DiscoveredProviderIds { get; }

    /// <summary>
    /// Gets the enabled source roots that were discovered for this snapshot.
    /// </summary>
    public IReadOnlyList<SourceRootRecord> SourceRoots { get; }

    /// <summary>
    /// Gets derived health and freshness details for the snapshot when available.
    /// </summary>
    public UsageTelemetrySnapshotHealth? Health { get; }
}

/// <summary>
/// Shared snapshot health summary used by tray and reports.
/// </summary>
public sealed class UsageTelemetrySnapshotHealth {
    /// <summary>
    /// Initializes a new instance of the <see cref="UsageTelemetrySnapshotHealth"/> class.
    /// </summary>
    /// <param name="isCachedSnapshot">Indicates whether the snapshot came from persisted cache instead of a live scan.</param>
    /// <param name="isPartialScan">Indicates whether some providers or roots were only partially scanned.</param>
    /// <param name="providerCount">The number of providers represented by the snapshot.</param>
    /// <param name="rootsCount">The number of discovered source roots represented by the snapshot.</param>
    /// <param name="accountLabels">The distinct account labels detected across providers.</param>
    /// <param name="eventsCount">The total number of usage events represented by the snapshot.</param>
    /// <param name="parsedArtifacts">The number of artifacts parsed during the scan.</param>
    /// <param name="reusedArtifacts">The number of cached artifacts reused during the scan.</param>
    /// <param name="duplicateRecordsCollapsed">The number of duplicate records collapsed during aggregation.</param>
    /// <param name="latestEventUtc">The latest event timestamp represented by the snapshot, when known.</param>
    /// <param name="issueCount">The number of non-fatal scan or discovery issues recorded for the snapshot.</param>
    /// <param name="providerHealth">Provider-scoped health details that compose this snapshot.</param>
    public UsageTelemetrySnapshotHealth(
        bool isCachedSnapshot,
        bool isPartialScan,
        int providerCount,
        int rootsCount,
        IReadOnlyList<string> accountLabels,
        int eventsCount,
        int parsedArtifacts,
        int reusedArtifacts,
        int duplicateRecordsCollapsed,
        DateTimeOffset? latestEventUtc,
        int issueCount,
        IReadOnlyList<UsageTelemetryProviderHealth>? providerHealth = null) {
        IsCachedSnapshot = isCachedSnapshot;
        IsPartialScan = isPartialScan;
        ProviderCount = Math.Max(0, providerCount);
        RootsCount = Math.Max(0, rootsCount);
        AccountLabels = accountLabels ?? Array.Empty<string>();
        AccountCount = AccountLabels.Count;
        EventsCount = Math.Max(0, eventsCount);
        ParsedArtifacts = Math.Max(0, parsedArtifacts);
        ReusedArtifacts = Math.Max(0, reusedArtifacts);
        DuplicateRecordsCollapsed = Math.Max(0, duplicateRecordsCollapsed);
        LatestEventUtc = latestEventUtc;
        IssueCount = Math.Max(0, issueCount);
        ProviderHealth = providerHealth ?? Array.Empty<UsageTelemetryProviderHealth>();
    }

    /// <summary>
    /// Gets a value indicating whether the snapshot was loaded from persisted cache.
    /// </summary>
    public bool IsCachedSnapshot { get; }

    /// <summary>
    /// Gets a value indicating whether one or more providers were only partially scanned.
    /// </summary>
    public bool IsPartialScan { get; }

    /// <summary>
    /// Gets the number of providers represented by the snapshot.
    /// </summary>
    public int ProviderCount { get; }

    /// <summary>
    /// Gets the number of discovered roots represented by the snapshot.
    /// </summary>
    public int RootsCount { get; }

    /// <summary>
    /// Gets the distinct account labels detected across providers.
    /// </summary>
    public IReadOnlyList<string> AccountLabels { get; }

    /// <summary>
    /// Gets the number of distinct account labels detected across providers.
    /// </summary>
    public int AccountCount { get; }

    /// <summary>
    /// Gets the total number of usage events represented by the snapshot.
    /// </summary>
    public int EventsCount { get; }

    /// <summary>
    /// Gets the number of artifacts parsed during the scan.
    /// </summary>
    public int ParsedArtifacts { get; }

    /// <summary>
    /// Gets the number of cached artifacts reused during the scan.
    /// </summary>
    public int ReusedArtifacts { get; }

    /// <summary>
    /// Gets the number of duplicate records collapsed during aggregation.
    /// </summary>
    public int DuplicateRecordsCollapsed { get; }

    /// <summary>
    /// Gets the latest event timestamp represented by the snapshot, when known.
    /// </summary>
    public DateTimeOffset? LatestEventUtc { get; }

    /// <summary>
    /// Gets the number of non-fatal discovery or scan issues recorded for the snapshot.
    /// </summary>
    public int IssueCount { get; }

    /// <summary>
    /// Gets provider-scoped health details that compose the snapshot.
    /// </summary>
    public IReadOnlyList<UsageTelemetryProviderHealth> ProviderHealth { get; }
}

/// <summary>
/// Provider-scoped health summary for one snapshot.
/// </summary>
public sealed class UsageTelemetryProviderHealth {
    /// <summary>
    /// Initializes a new instance of the <see cref="UsageTelemetryProviderHealth"/> class.
    /// </summary>
    /// <param name="providerId">The provider identifier represented by this health entry.</param>
    /// <param name="rootsCount">The number of discovered roots that produced data for the provider.</param>
    /// <param name="accountLabels">The distinct account labels detected for the provider.</param>
    /// <param name="eventsCount">The number of usage events represented for the provider.</param>
    /// <param name="parsedArtifacts">The number of artifacts parsed for the provider.</param>
    /// <param name="reusedArtifacts">The number of cached artifacts reused for the provider.</param>
    /// <param name="duplicateRecordsCollapsed">The number of duplicate records collapsed for the provider.</param>
    /// <param name="latestEventUtc">The latest provider event timestamp, when known.</param>
    /// <param name="isPartialScan">Indicates whether the provider data came from a partial scan.</param>
    public UsageTelemetryProviderHealth(
        string providerId,
        int rootsCount,
        IReadOnlyList<string> accountLabels,
        int eventsCount,
        int parsedArtifacts,
        int reusedArtifacts,
        int duplicateRecordsCollapsed,
        DateTimeOffset? latestEventUtc,
        bool isPartialScan) {
        ProviderId = string.IsNullOrWhiteSpace(providerId) ? "unknown" : providerId.Trim();
        RootsCount = Math.Max(0, rootsCount);
        AccountLabels = accountLabels ?? Array.Empty<string>();
        AccountCount = AccountLabels.Count;
        EventsCount = Math.Max(0, eventsCount);
        ParsedArtifacts = Math.Max(0, parsedArtifacts);
        ReusedArtifacts = Math.Max(0, reusedArtifacts);
        DuplicateRecordsCollapsed = Math.Max(0, duplicateRecordsCollapsed);
        LatestEventUtc = latestEventUtc;
        IsPartialScan = isPartialScan;
    }

    /// <summary>
    /// Gets the provider identifier represented by this health entry.
    /// </summary>
    public string ProviderId { get; }

    /// <summary>
    /// Gets the number of discovered roots that produced data for the provider.
    /// </summary>
    public int RootsCount { get; }

    /// <summary>
    /// Gets the distinct account labels detected for the provider.
    /// </summary>
    public IReadOnlyList<string> AccountLabels { get; }

    /// <summary>
    /// Gets the number of distinct account labels detected for the provider.
    /// </summary>
    public int AccountCount { get; }

    /// <summary>
    /// Gets the number of usage events represented for the provider.
    /// </summary>
    public int EventsCount { get; }

    /// <summary>
    /// Gets the number of artifacts parsed for the provider.
    /// </summary>
    public int ParsedArtifacts { get; }

    /// <summary>
    /// Gets the number of cached artifacts reused for the provider.
    /// </summary>
    public int ReusedArtifacts { get; }

    /// <summary>
    /// Gets the number of duplicate records collapsed for the provider.
    /// </summary>
    public int DuplicateRecordsCollapsed { get; }

    /// <summary>
    /// Gets the latest provider event timestamp, when known.
    /// </summary>
    public DateTimeOffset? LatestEventUtc { get; }

    /// <summary>
    /// Gets a value indicating whether the provider data came from a partial scan.
    /// </summary>
    public bool IsPartialScan { get; }
}
