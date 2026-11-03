using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Telemetry.GitHub;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class PullRequestCatalogTests {
    [Fact]
    public async Task CatalogRetainsExactLinksAndFailedReadTimesAndProgressesAtCapacityAfterDurableMergedAdmission() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-pr-catalog", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using var source = new GitHubDashboardService("synthetic-token", $"http://127.0.0.1:{endpoint.Port}/");
        async Task Reply(string state, int status = 200, int number = 7) {
            using var socket = await listener.AcceptTcpClientAsync();
            using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            Assert.Equal($"GET /repos/Sample/Project/pulls/{number} HTTP/1.1", await reader.ReadLineAsync());
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { state, merged = state == "closed",
                html_url = "https://github.com/Sample/Project/pull/7", title = "private-title-sentinel", body = "private-body-sentinel",
                @base = new { repo = new { full_name = "Sample/Project" } }, head = new { @ref = "feature/example" } }));
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Fixture\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
            await stream.WriteAsync(body);
        }
        try {
            var catalog = new PullRequestCatalog(directory, source);
            var response = Reply("open");
            var first = Assert.Single(await catalog.RefreshAsync(["https://github.com/Sample/Project/pull/7"], default));
            await response;
            Assert.Equal("open", first.State);
            catalog = new(directory, source);
            response = Reply("unknown");
            Assert.Equal(first, Assert.Single(await catalog.RefreshAsync([], default)));
            await response;
            Assert.True(catalog.ReadIncomplete);
            response = Reply("closed", 503);
            Assert.Equal(first, Assert.Single(await catalog.RefreshAsync([], default)));
            await response;
            // A long-lived catalog at capacity must still refresh known open work.
            var path = Path.Combine(directory, "pull-requests.catalog");
            var seeded = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            for (var number = 100; number < 1099; number++) {
                var url = "https://github.com/Sample/Project/pull/" + number;
                var id = LocalReporter.ItemId("pr", "github", url);
                seeded[id] = JsonSerializer.SerializeToNode(new { url, proof = new PullRequestProof(id, "merged", first.CheckedAtUtc) }, LedgerJson.Options);
            }
            File.WriteAllText(path, seeded.ToJsonString());
            catalog = new(directory, source);
            response = Reply("closed");
            var proofs = await catalog.RefreshAsync(["https://github.com/Sample/Project/pull/8"], default);
            var merged = Assert.Single(proofs, proof => proof.Id == first.Id);
            await response;
            Assert.Equal("merged", merged.State);
            Assert.True(catalog.ReadIncomplete);
            Assert.Equal(1000, (await new PullRequestCatalog(directory, source).RefreshAsync([], default)).Length);
            using var spool = new ReporterSpool(directory, 1024 * 1024);
            spool.Append(new() { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1,
                ItemId = "project-evidence", MachineId = "synthetic-machine", ReporterId = "synthetic-reporter", Provider = "project",
                Source = "watcher", ObservedAtUtc = DateTimeOffset.UtcNow, GlobalWork = new(null, [], null, [], proofs) });
            catalog.RetireAdmittedMerged(proofs);
            Assert.Equal(1000, Assert.Single(spool.Pending()).GlobalWork!.PullRequestProofs!.Length);
            catalog = new(directory, source);
            response = Reply("open", number: 8);
            var rediscovered = Enumerable.Range(100, 999).Select(number => "https://github.com/Sample/Project/pull/" + number)
                .Append("https://github.com/Sample/Project/pull/7").Append("https://github.com/Sample/Project/pull/8");
            Assert.Equal("open", Assert.Single(await catalog.RefreshAsync(rediscovered, default)).State);
            await response;
            Assert.False(catalog.ReadIncomplete);
            var stored = File.ReadAllText(path);
            Assert.DoesNotContain("sentinel", stored);
            Assert.DoesNotContain("synthetic-token", stored);
        } finally { listener.Stop(); Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("https://foreign.example/Sample/Project/pull/7")]
    [InlineData("https://secret@github.com/Sample/Project/pull/7")]
    [InlineData("https://github.com/Sample/Project/pull/7?token=secret")]
    public void NonCanonicalLinksAreNotGitHubMetadataTargets(string url) => Assert.False(PullRequestCatalog.TryParse(url, out _, out _));
}
