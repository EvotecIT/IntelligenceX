using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Tray.Services;

/// <summary>
/// Wraps the IX telemetry APIs to discover, scan, and aggregate usage data.
/// </summary>
public sealed class UsageDataService {
    /// <summary>
    /// Scans all known provider roots and returns usage events.
    /// </summary>
    public async Task<UsageDataSnapshot> ScanAsync(CancellationToken cancellationToken = default) {
        var discoveries = UsageTelemetryProviderCatalog.CreateRootDiscoveries();
        var roots = new List<SourceRootRecord>();
        foreach (var discovery in discoveries) {
            try {
                var discovered = discovery.DiscoverRoots();
                roots.AddRange(discovered);
            } catch {
                // Skip providers whose root discovery fails (e.g. missing directories).
            }
        }

        if (roots.Count == 0) {
            return UsageDataSnapshot.Empty;
        }

        var scanner = new UsageTelemetryQuickReportScanner();
        var result = await scanner.ScanAsync(roots, cancellationToken: cancellationToken);

        return new UsageDataSnapshot(result.Events, DateTimeOffset.UtcNow);
    }
}

/// <summary>
/// A point-in-time snapshot of scanned usage data.
/// </summary>
public sealed class UsageDataSnapshot {
    public static readonly UsageDataSnapshot Empty = new([], DateTimeOffset.UtcNow);

    public UsageDataSnapshot(IReadOnlyList<UsageEventRecord> events, DateTimeOffset scannedAtUtc) {
        Events = events;
        ScannedAtUtc = scannedAtUtc;
    }

    public IReadOnlyList<UsageEventRecord> Events { get; }
    public DateTimeOffset ScannedAtUtc { get; }
}
