using IntelligenceX.OpenAI;
using IntelligenceX.Shared;
using IntelligenceX.Telemetry.GitHub;
using IntelligenceX.Telemetry.Limits;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Tools;

var query = new GitHubNotificationQuery { Limit = 5 };
using var inbox = new GitHubNotificationService("package-smoke-token");
var clientOptions = new IntelligenceXClientOptions();
var snapshot = new UsageTelemetrySnapshot(
    Array.Empty<UsageEventRecord>(),
    DateTimeOffset.UtcNow,
    rootsFound: 0,
    scanDurationMs: 0,
    errors: Array.Empty<string>());
var limits = new ProviderLimitSnapshot(
    providerId: "smoke",
    displayName: "Smoke",
    sourceLabel: "package",
    planLabel: null,
    accountLabel: null,
    windows: Array.Empty<ProviderLimitWindow>(),
    summary: null,
    detailMessage: null,
    retrievedAtUtc: DateTimeOffset.UtcNow);
var packId = ToolPackIdentityCatalog.NormalizePackId("ad");
var domainLabel = DomainIntentFamilyPresentationCatalog.ResolveDisplayName("ad_domain");

#if NET472
Console.WriteLine($"IntelligenceX package smoke passed: transport={clientOptions.TransportKind}, inbox={inbox.GetType().Name}, query={query.Limit}, events={snapshot.Events.Count}, limits={limits.IsAvailable}, pack={packId}, domain={domainLabel}");
#else
var databaseDirectory = Path.Combine(Path.GetTempPath(), "intelligencex-package-smoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(databaseDirectory);
try {
    using var store = new SqliteUsageEventStore(Path.Combine(databaseDirectory, "smoke.db"));
    Console.WriteLine($"IntelligenceX package smoke passed: transport={clientOptions.TransportKind}, inbox={inbox.GetType().Name}, query={query.Limit}, events={snapshot.Events.Count}, limits={limits.IsAvailable}, pack={packId}, domain={domainLabel}, store={store.GetType().Name}");
} finally {
    Directory.Delete(databaseDirectory, recursive: true);
}
#endif
