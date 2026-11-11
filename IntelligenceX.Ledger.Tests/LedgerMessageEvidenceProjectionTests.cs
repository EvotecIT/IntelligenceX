using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using DBAClientX;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Ledger.Rules;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerMessageEvidenceProjectionTests {
    [Fact]
    public async Task HttpWorkReadsExcludeMessageReceiptsWhileExactLookupAndOrdinaryWorkRemain() {
        var directory = LedgerTestPaths.NewDirectory("message-projection-http");
        Directory.CreateDirectory(directory);
        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        var settings = new LedgerSettings {
            DataDirectory = directory, Port = port, ReadToken = new('r', 32), WriteToken = new('w', 32),
            ExpectedReporters = new() { ["synthetic-machine"] = ["synthetic-reporter"] }
        };
        var config = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(settings, LedgerJson.Options));
        var start = new ProcessStartInfo("dotnet") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("serve");
        start.ArgumentList.Add("--config");
        start.ArgumentList.Add(config);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            using var client = new HttpClient {
                BaseAddress = new Uri($"http://127.0.0.1:{port}/api/ledger/v1/"), Timeout = Timeout.InfiniteTimeSpan
            };
            HttpResponseMessage? ready = null;
            while (ready is null) {
                if (process.HasExited) throw new InvalidOperationException(await error);
                try { ready = await client.GetAsync("status", timeout.Token); }
                catch (HttpRequestException) { await Task.Delay(50, timeout.Token); }
            }
            using (ready) Assert.Equal(HttpStatusCode.Unauthorized, ready.StatusCode);
            var now = DateTimeOffset.UtcNow;
            var peer = Observation("synthetic-session", now) with {
                Session = new() {
                    SourceSessionId = Guid.NewGuid(), SourceMessageId = Guid.NewGuid(), TurnId = "synthetic-current-turn",
                    TurnOpen = true, TurnStartedAtUtc = now, LastActivityAtUtc = now
                }
            };
            var completed = peer with { Session = peer.Session with {
                SourceMessageId = Guid.NewGuid(), TurnOpen = false, Outcome = "completed", TurnEndedAtUtc = now
            } };
            var quota = peer with { Session = peer.Session with {
                SourceMessageId = Guid.NewGuid(), TurnOpen = false, Outcome = "failed", UsageLimit = true,
                TurnStartedAtUtc = now.AddDays(-2), TurnEndedAtUtc = now.AddDays(-2), LastActivityAtUtc = now.AddDays(-2)
            } };
            var receipts = LocalReporter.SessionMessageEvents(completed).Concat(LocalReporter.SessionMessageEvents(quota)).ToArray();
            var events = new[] {
                peer,
                Observation("synthetic-held-delivery", now) with { Delivery = new("synthetic-delivery", "held", now) },
                Observation("synthetic-input", now) with { ItemKind = "input", Session = new() {
                    SourceMessageId = Guid.NewGuid(), InputId = "synthetic-question", Question = true, QuestionAtUtc = now
                } },
                Observation("synthetic-ordinary-delivery", now) with { ItemKind = "delivery", Session = new() {
                    TurnOpen = false, Outcome = "completed", TurnEndedAtUtc = now, LastActivityAtUtc = now
                } },
                Observation("machine", now) with { Heartbeat = true }
            }.Concat(receipts).Select((e, index) => e with { Sequence = index + 1 }).ToArray();
            client.DefaultRequestHeaders.Authorization = new("Bearer", settings.WriteToken);
            using (var ingested = await client.PostAsJsonAsync("events", events, LedgerJson.Options, timeout.Token))
                Assert.Equal(HttpStatusCode.OK, ingested.StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", settings.ReadToken);
            using var reader = new LedgerReadClient(new() { Endpoint = $"http://127.0.0.1:{port}/", ReadToken = settings.ReadToken });
            foreach (var receipt in receipts.Where(e => e.Session is not null)) {
                var item = await reader.ItemAsync(receipt.ItemId, timeout.Token);
                Assert.Equal("delivery", item.Kind);
                Assert.Equal(receipt.Session!.UsageLimit ? "usage-limit" : "unknown", item.ReasonCode);
                Assert.Contains(item.Evidence, evidence => evidence.EventId == receipt.EventId);
                var terminal = item.DeliveryStages[receipt.ItemId].Stages["terminal"];
                Assert.Equal(receipt.Session.Outcome, terminal.Outcome);
                using var history = await client.GetFromJsonAsync<JsonDocument>("items/" + receipt.ItemId + "/events", timeout.Token);
                Assert.Equal(3, history!.RootElement.GetArrayLength());
                Assert.Equal(receipt.Session.SourceMessageId, history.RootElement[0].GetProperty("observation")
                    .GetProperty("session").GetProperty("sourceMessageId").GetGuid());
            }
            using var status = await client.GetFromJsonAsync<JsonDocument>("status", timeout.Token);
            var counts = status!.RootElement.GetProperty("counts");
            Assert.Equal(4, counts.EnumerateObject().Sum(pair => pair.Value.GetInt32()));
            Assert.Equal(1, counts.GetProperty("working").GetInt32());
            Assert.Equal(1, counts.GetProperty("needs-you").GetInt32());
            Assert.Equal(2, counts.GetProperty("stuck").GetInt32());
            var work = await reader.WorkAsync(timeout.Token);
            string[] expected = ["synthetic-held-delivery", "synthetic-input", "synthetic-ordinary-delivery", "synthetic-session"];
            Assert.Equal(expected, work.Items.Select(row => row.Item.Id).Order(StringComparer.Ordinal));
            Assert.Equal(4, work.Counts.Values.Sum());
            Assert.Equal("working", Assert.Single(work.Items, row => row.Item.Id == peer.ItemId).Item.Verdict);
            Assert.Equal("wake-held", Assert.Single(work.Items, row => row.Item.Id == "synthetic-held-delivery").Item.ReasonCode);
            var paged = new List<string>();
            string? cursor = null;
            do {
                var route = "items?limit=1" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
                using var page = await client.GetFromJsonAsync<JsonDocument>(route, timeout.Token);
                paged.Add(Assert.Single(page!.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetString()!);
                cursor = page.RootElement.GetProperty("nextCursor").GetString();
            } while (cursor is not null);
            Assert.Equal(expected, paged.Order(StringComparer.Ordinal));
        } finally {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await Task.WhenAll(output, error);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReceiptIngestionAndReevaluationRetainDiagnosticsWithoutHumanTransitions() {
        var directory = LedgerTestPaths.NewDirectory("message-transitions");
        var path = Path.Combine(directory, "ledger.sqlite");
        try {
            using var store = new LedgerStore(path, new(new()));
            await store.InitializeAsync();
            var now = new DateTimeOffset(2030, 1, 2, 12, 0, 0, TimeSpan.Zero);
            var session = Observation("synthetic-session", now) with { Session = new() {
                SourceMessageId = Guid.NewGuid(), TurnOpen = true, TurnStartedAtUtc = now, LastActivityAtUtc = now
            } };
            var events = LocalReporter.SessionMessageEvents(session).Prepend(session)
                .Select((e, index) => e with { Sequence = index + 1 }).ToArray();
            await store.IngestAsync(events, now);
            var later = now.AddHours(1);
            var rows = await store.EvaluateAllAsync(later);
            var receipt = Assert.Single(rows, row => row.Kind == "delivery");
            Assert.Equal("no-progress", receipt.Current!.ReasonCode);
            Assert.Equal(later, receipt.Current.EvaluatedAtUtc);
            Assert.Equal(2, (await store.HistoryAsync(receipt.Id, 0, 10, default)).Length);
            using var database = new SQLite();
            await using var db = await database.OpenSessionAsync(path);
            var transitions = await db.QueryAsListAsync("SELECT item FROM transitions ORDER BY ordinal", r => r.GetString(0));
            Assert.Equal(new[] { session.ItemId, session.ItemId }, transitions);
            var stored = await db.QueryAsListAsync("SELECT json FROM items WHERE id=@id", r => r.GetString(0),
                new Dictionary<string, object?> { ["id"] = receipt.Id });
            using var persisted = JsonDocument.Parse(Assert.Single(stored));
            Assert.False(persisted.RootElement.TryGetProperty("isMessageEvidence", out _));
            Assert.Equal("no-progress", persisted.RootElement.GetProperty("current").GetProperty("reasonCode").GetString());
        } finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task DailyReportFiltersPersistedReceiptsAndKeepsRangeOrderMarksAndOrphanHistory() {
        var directory = LedgerTestPaths.NewDirectory("message-report");
        var path = Path.Combine(directory, "ledger.sqlite");
        try {
            using var store = new LedgerStore(path, new(new()));
            await store.InitializeAsync();
            var now = new DateTimeOffset(2030, 1, 2, 12, 0, 0, TimeSpan.Zero);
            var completed = Observation("synthetic-session", now) with { Session = new() {
                SourceMessageId = Guid.NewGuid(), TurnOpen = false, Outcome = "completed",
                TurnStartedAtUtc = now.AddMinutes(-1), TurnEndedAtUtc = now, LastActivityAtUtc = now
            } };
            var events = LocalReporter.SessionMessageEvents(completed).Prepend(completed)
                .Select((e, index) => e with { Sequence = index + 1 }).ToArray();
            await store.IngestAsync(events, now);
            var receiptId = events[1].ItemId;
            using var database = new SQLite();
            await using var db = await database.OpenSessionAsync(path);
            // Simulate transitions already written by the earlier host, including a row with no current item.
            await db.ExecuteNonQueryAsync("""
                INSERT INTO transitions(ordinal,item,recorded,json) VALUES
                (10001,@receipt,'2030-01-02T00:00:00.0000000+00:00','{"id":"synthetic-old-receipt"}'),
                (10002,'synthetic-orphan','2030-01-02T00:00:00.0000000+00:00','{"id":"synthetic-orphan"}'),
                (10003,'synthetic-session','2030-01-02T00:00:00.0000000+00:00','{"id":"synthetic-session"}'),
                (10004,'synthetic-orphan','2030-01-03T00:00:00.0000000+00:00','{"id":"synthetic-next-day"}'),
                (10005,@receipt,'2030-01-02T23:59:59.0000000+00:00','{"id":"synthetic-old-failure"}');
                INSERT INTO reviews(transition,mark,recorded) VALUES
                (10001,'wrong','2030-01-02T00:01:00.0000000+00:00'),
                (10003,'right','2030-01-02T00:01:00.0000000+00:00');
                """, new Dictionary<string, object?> { ["receipt"] = receiptId });
            var report = JsonSerializer.SerializeToElement(await store.ReportAsync(new(2030, 1, 2), now, default), LedgerJson.Options);
            Assert.Equal(completed.ItemId, Assert.Single(report.GetProperty("unresolvedUnknowns").EnumerateArray()).GetProperty("id").GetString());
            Assert.Equal(4, report.GetProperty("observationCoverage").GetProperty("eventCount").GetInt64());
            var daily = JsonSerializer.SerializeToElement(await store.DailyAsync(new(2030, 1, 2), default), LedgerJson.Options);
            Assert.Equal(new long[] { 1, 10002, 10003 }, daily.EnumerateArray().Select(row => row.GetProperty("transitionId").GetInt64()));
            Assert.Equal("unmarked", daily[1].GetProperty("mark").GetString());
            Assert.Equal("right", daily[2].GetProperty("mark").GetString());
            Assert.Equal(daily.GetRawText(), report.GetProperty("transitions").GetRawText());
            var retained = await db.QueryAsListAsync("SELECT ordinal FROM transitions WHERE item=@id AND ordinal>=10000 ORDER BY ordinal",
                r => r.GetInt64(0), new Dictionary<string, object?> { ["id"] = receiptId });
            Assert.Equal(new long[] { 10001, 10005 }, retained);
            Assert.Equal("wrong", await db.ExecuteScalarAsync("SELECT mark FROM reviews WHERE transition=10001"));
            var plan = await db.QueryAsListAsync("""
                EXPLAIN QUERY PLAN SELECT t.ordinal,t.json,r.mark,i.json FROM transitions t
                LEFT JOIN reviews r ON r.transition=t.ordinal LEFT JOIN items i ON i.id=t.item
                WHERE t.recorded>=@start AND t.recorded<@end ORDER BY t.ordinal
                """, r => r.GetString(3), new Dictionary<string, object?> {
                    ["start"] = "2030-01-02T00:00:00.0000000+00:00", ["end"] = "2030-01-03T00:00:00.0000000+00:00"
                });
            Assert.Contains(plan, row => row.Contains("SEARCH t USING INDEX transitions_recorded", StringComparison.Ordinal));
            Assert.DoesNotContain(plan, row => row.StartsWith("SCAN t", StringComparison.Ordinal));
        } finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static LedgerEvent Observation(string id, DateTimeOffset now) => new() {
        EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1, ReporterId = "synthetic-reporter",
        MachineId = "synthetic-machine", ItemId = id, Provider = "codex", Source = "watcher", ObservedAtUtc = now
    };
}
