using System.Diagnostics;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Tray.Services;

/// <summary>
/// Wraps the IX telemetry APIs to discover, scan, and aggregate usage data.
/// Scans each provider separately with its own artifact budget to ensure
/// all providers get represented even when one has thousands of files.
/// </summary>
public sealed class UsageDataService {
    public async Task<UsageDataSnapshot> ScanAsync(CancellationToken cancellationToken = default) {
        var discoveries = UsageTelemetryProviderCatalog.CreateRootDiscoveries();
        var allRoots = new List<SourceRootRecord>();
        var errors = new List<string>();

        foreach (var discovery in discoveries) {
            try {
                allRoots.AddRange(discovery.DiscoverRoots());
            } catch (Exception ex) {
                errors.Add($"{discovery.ProviderId}: {ex.Message}");
            }
        }

        if (allRoots.Count == 0) {
            return new UsageDataSnapshot([], DateTimeOffset.UtcNow, 0, 0, errors);
        }

        // Group roots by provider and scan each separately with its own budget.
        var rootsByProvider = allRoots
            .Where(r => r.Enabled)
            .GroupBy(r => r.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allEvents = new List<UsageEventRecord>();
        var sw = Stopwatch.StartNew();

        foreach (var providerRoots in rootsByProvider) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                var scanner = new UsageTelemetryQuickReportScanner();
                var options = new UsageTelemetryQuickReportOptions {
                    PreferRecentArtifacts = true,
                    MaxArtifacts = 200
                };
                var result = await scanner.ScanAsync(providerRoots.ToList(), options, cancellationToken);
                allEvents.AddRange(result.Events);
            } catch (Exception ex) {
                errors.Add($"{providerRoots.Key}: scan failed - {ex.Message}");
            }
        }

        sw.Stop();

        return new UsageDataSnapshot(
            allEvents,
            DateTimeOffset.UtcNow,
            allRoots.Count,
            sw.ElapsedMilliseconds,
            errors);
    }
}

public sealed class UsageDataSnapshot {
    public UsageDataSnapshot(
        IReadOnlyList<UsageEventRecord> events,
        DateTimeOffset scannedAtUtc,
        int rootsFound,
        long scanDurationMs,
        IReadOnlyList<string> errors) {
        Events = events;
        ScannedAtUtc = scannedAtUtc;
        RootsFound = rootsFound;
        ScanDurationMs = scanDurationMs;
        Errors = errors;
    }

    public IReadOnlyList<UsageEventRecord> Events { get; }
    public DateTimeOffset ScannedAtUtc { get; }
    public int RootsFound { get; }
    public long ScanDurationMs { get; }
    public IReadOnlyList<string> Errors { get; }
}
