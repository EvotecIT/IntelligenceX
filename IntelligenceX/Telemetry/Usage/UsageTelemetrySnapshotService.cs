using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Shared snapshot service for lightweight usage discovery and aggregation.
/// </summary>
public sealed class UsageTelemetrySnapshotService {
    private const int TrayMaxArtifacts = 60;
    private readonly Func<IReadOnlyList<IUsageTelemetryRootDiscovery>> _createDiscoveries;
    private readonly Func<UsageTelemetryQuickReportScanner> _createScanner;

    /// <summary>
    /// Initializes a new shared snapshot service.
    /// </summary>
    public UsageTelemetrySnapshotService()
        : this(
            () => UsageTelemetryProviderCatalog.CreateRootDiscoveries(),
            static () => new UsageTelemetryQuickReportScanner()) {
    }

    internal UsageTelemetrySnapshotService(
        Func<IReadOnlyList<IUsageTelemetryRootDiscovery>> createDiscoveries,
        Func<UsageTelemetryQuickReportScanner> createScanner) {
        _createDiscoveries = createDiscoveries ?? throw new ArgumentNullException(nameof(createDiscoveries));
        _createScanner = createScanner ?? throw new ArgumentNullException(nameof(createScanner));
    }

    /// <summary>
    /// Discovers usage roots and returns a provider-grouped quick snapshot.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>Usage snapshot with events, timing, and any non-fatal discovery errors.</returns>
    public async Task<UsageTelemetrySnapshot> ScanAsync(CancellationToken cancellationToken = default) {
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

        if (allRoots.Count == 0) {
            return new UsageTelemetrySnapshot(
                Array.Empty<UsageEventRecord>(),
                DateTimeOffset.UtcNow,
                0,
                0,
                errors);
        }

        var rootsByProvider = allRoots
            .Where(static root => root.Enabled)
            .GroupBy(static root => root.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var allEvents = new List<UsageEventRecord>();
        var stopwatch = Stopwatch.StartNew();

        foreach (var providerRoots in rootsByProvider) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                var scanner = _createScanner();
                var options = new UsageTelemetryQuickReportOptions {
                    PreferRecentArtifacts = true,
                    MaxArtifacts = TrayMaxArtifacts
                };
                var result = await scanner
                    .ScanAsync(providerRoots.ToArray(), options, cancellationToken)
                    .ConfigureAwait(false);
                allEvents.AddRange(result.Events);
            } catch (Exception ex) {
                errors.Add(providerRoots.Key + ": scan failed - " + ex.Message);
            }
        }

        stopwatch.Stop();

        return new UsageTelemetrySnapshot(
            allEvents,
            DateTimeOffset.UtcNow,
            allRoots.Count,
            stopwatch.ElapsedMilliseconds,
            errors);
    }
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
        IReadOnlyList<string> errors) {
        Events = events ?? Array.Empty<UsageEventRecord>();
        ScannedAtUtc = scannedAtUtc;
        RootsFound = Math.Max(0, rootsFound);
        ScanDurationMs = Math.Max(0L, scanDurationMs);
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>
    /// Gets the discovered usage events.
    /// </summary>
    public IReadOnlyList<UsageEventRecord> Events { get; }

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
}
