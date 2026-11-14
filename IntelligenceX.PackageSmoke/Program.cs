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
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
listener.Start();
var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
const string responseJson = """
    {"asOfUtc":"2026-01-01T00:00:00Z","rulesVersion":3,"machines":[],"counts":{"working":2},"items":[],"coverage":[],"truncated":false}
    """;
var responseBytes = System.Text.Encoding.UTF8.GetBytes(responseJson);
var serving = Task.Run(async () => {
    using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
    await using var stream = socket.GetStream();
    using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
    if (await reader.ReadLineAsync(deadline.Token) != "GET /api/ledger/v1/work?limit=200 HTTP/1.1") {
        throw new InvalidOperationException("The packaged Ledger reader used an unexpected work route.");
    }
    var authorized = false;
    string? header;
    while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(deadline.Token))) {
        authorized |= header == "Authorization: Bearer " + new string('r', 32);
    }
    if (!authorized) throw new InvalidOperationException("The packaged Ledger reader omitted its configured credential.");
    var headers = System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " +
        responseBytes.Length + "\r\nConnection: close\r\n\r\n");
    await stream.WriteAsync(headers, deadline.Token);
    await stream.WriteAsync(responseBytes, deadline.Token);
}, deadline.Token);
using var ledger = new IntelligenceX.Ledger.Contracts.LedgerReadClient(new() {
    Endpoint = $"http://127.0.0.1:{port}/", ReadToken = new('r', 32)
});
var ledgerWork = await ledger.WorkAsync(deadline.Token);
await serving;
if (ledgerWork.RulesVersion != 3 || ledgerWork.Counts["working"] != 2 || ledgerWork.Items.Length != 0) {
    throw new InvalidOperationException("The packaged Ledger reader did not preserve the fixture's work state.");
}
var databaseDirectory = Path.Combine(Path.GetTempPath(), "intelligencex-package-smoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(databaseDirectory);
try {
    using var store = new SqliteUsageEventStore(Path.Combine(databaseDirectory, "smoke.db"));
    Console.WriteLine($"IntelligenceX package smoke passed: transport={clientOptions.TransportKind}, inbox={inbox.GetType().Name}, query={query.Limit}, events={snapshot.Events.Count}, limits={limits.IsAvailable}, pack={packId}, domain={domainLabel}, store={store.GetType().Name}, ledgerRules={ledgerWork.RulesVersion}");
} finally {
    Directory.Delete(databaseDirectory, recursive: true);
}
#endif
