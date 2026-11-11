using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Context;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Reporting;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerHttpTests {
    [Fact]
    public async Task RealHostEnforcesTokenCapabilitiesAndRejectsPayloadFieldsBeforePersistence() {
        var directory = LedgerTestPaths.NewDirectory("http");
        Directory.CreateDirectory(directory);
        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        var source = Path.Combine(directory, "experiment.md");
        await File.WriteAllTextAsync(source, "# Rejected experiment\nBuffered encoding increased allocations and was rejected.");
        var catalog = Path.Combine(directory, "catalog.json");
        await File.WriteAllTextAsync(catalog, JsonSerializer.Serialize(new ProjectCatalog {
            ObservedAtUtc = DateTimeOffset.UtcNow,
            Projects = [new() { Id = "synthetic-project", Name = "Synthetic project", Repository = "example/project",
                Sources = [new() { Id = "synthetic-experiment", Title = "Synthetic experiment", Path = source,
                    SourceRef = "https://example.test/experiment", Outcome = "rejected" }] }]
        }, LedgerJson.Options));
        var settings = new LedgerSettings { DataDirectory = directory, Port = port,
            ProjectCatalogPath = catalog,
            ReadToken = new('r', 32), WriteToken = new('w', 32), ProjectEvidenceReporterId = "synthetic-reporter",
            ExpectedReporters = new() { ["synthetic-machine"] = ["synthetic-reporter"] } };
        var config = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(settings, LedgerJson.Options));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(Program).Assembly.Location); start.ArgumentList.Add("serve");
        start.ArgumentList.Add("--config"); start.ArgumentList.Add(config);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try {
            // Startup and the complete multi-request scenario share this budget.
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/api/ledger/v1/"), Timeout = Timeout.InfiniteTimeSpan };
            HttpResponseMessage? ready = null;
            while (ready is null) {
                if (process.HasExited) throw new InvalidOperationException(await error);
                try { ready = await client.GetAsync("status", timeout.Token); }
                catch (HttpRequestException) { await Task.Delay(50, timeout.Token); }
            }
            using (ready) Assert.Equal(HttpStatusCode.Unauthorized, ready.StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", settings.WriteToken);
            using (var denied = await client.GetAsync("status", timeout.Token)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            var observation = new LedgerEvent { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1,
                ReporterId = "synthetic-reporter", MachineId = "synthetic-machine", ItemId = "synthetic-item", Provider = "codex",
                Source = "evidence", ObservedAtUtc = DateTimeOffset.UtcNow, Delivery = new("synthetic-delivery", "submitted", DateTimeOffset.UtcNow) };
            var serialized = JsonSerializer.Serialize(observation, LedgerJson.Options);
            var invalid = "[" + serialized[..^1] + ",\"secret\":\"synthetic-sentinel\"}]";
            using (var rejected = await client.PostAsync("events", new StringContent(invalid, Encoding.UTF8, "application/json"), timeout.Token))
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            var invalidWork = JsonSerializer.Serialize(new[] { observation with { Delivery = null, Work = new() { OpenPullRequests = null! } } }, LedgerJson.Options);
            using (var rejected = await client.PostAsync("events", new StringContent(invalidWork, Encoding.UTF8, "application/json"), timeout.Token))
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            using (var rejected = await client.PostAsJsonAsync("events", new[] { observation with { MachineId = null! } }, LedgerJson.Options, timeout.Token))
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            var unbound = observation with { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Delivery = null, Declaration = new() {
                Id = "synthetic-declaration", EpisodeId = "synthetic-episode", Scope = new("synthetic-task", 1),
                Kind = "operation", CheckpointRef = "synthetic-checkpoint", ConditionRef = "synthetic-condition",
                NextActor = "worker", Blocking = true, RecordedAtUtc = observation.ObservedAtUtc, ReviewAfterUtc = observation.ObservedAtUtc.AddHours(1)
            } };
            using (var rejected = await client.PostAsJsonAsync("events", new[] { unbound }, LedgerJson.Options, timeout.Token))
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            using (var accepted = await client.PostAsJsonAsync("events", new[] { observation }, LedgerJson.Options, timeout.Token))
                Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            using (var replay = await client.PostAsJsonAsync("events", new[] { observation }, LedgerJson.Options, timeout.Token))
                Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            using (var conflict = await client.PostAsJsonAsync("events", new[] { observation with { Sequence = 2 } }, LedgerJson.Options, timeout.Token))
                Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ReadToken);
            using var reader = new LedgerReadClient(new() { Endpoint = $"http://127.0.0.1:{port}/", ReadToken = settings.ReadToken });
            var project = await reader.ProjectAsync("synthetic-project", "buffered encoding", timeout.Token);
            var evidence = Assert.Single(project.Knowledge);
            Assert.Equal("rejected", evidence.Outcome);
            Assert.Equal("https://example.test/experiment", evidence.SourceRef);
            Assert.Contains("increased allocations", evidence.Excerpt);
            using (var denied = await client.PostAsJsonAsync("events", new[] { observation }, LedgerJson.Options, timeout.Token))
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            using (var invalidQuery = await client.GetAsync("/api/coordination/v1/knowledge?project=synthetic-project&query=%3F%3F", timeout.Token))
                Assert.Equal(HttpStatusCode.BadRequest, invalidQuery.StatusCode);
            var work = await reader.WorkAsync(timeout.Token);
            Assert.Equal("synthetic-item", Assert.Single(work.Items).Item.Id);
            using var itemResponse = await client.GetAsync("items/synthetic-item", timeout.Token);
            itemResponse.EnsureSuccessStatusCode();
            using var item = JsonDocument.Parse(await itemResponse.Content.ReadAsStringAsync(timeout.Token));
            Assert.Equal("stuck", item.RootElement.GetProperty("verdict").GetString());
            Assert.Equal("unknown", item.RootElement.GetProperty("deliveryStages").GetProperty("synthetic-delivery")
                .GetProperty("stages").GetProperty("started").GetProperty("knowledge").GetString());
            using var history = await client.GetAsync("items/synthetic-item/events", timeout.Token);
            using var events = JsonDocument.Parse(await history.Content.ReadAsStringAsync(timeout.Token));
            Assert.Equal(1, events.RootElement.GetArrayLength());
            var large = observation with { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), ItemId = "project-evidence", Delivery = null,
                GlobalWork = new(DateTimeOffset.UtcNow, Enumerable.Range(0, 10000).Select(i => "synthetic-" + i.ToString("D6") + new string('a', 48)).ToArray(), null, []) };
            var second = large with { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid() };
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(new[] { large, second }, LedgerJson.Options).Length > LedgerJson.MaximumBatchBytes);
            using var spool = new ReporterSpool(Path.Combine(directory, "outbox"), 3 * LedgerJson.MaximumBatchBytes);
            spool.Append(large); spool.Append(second);
            Assert.Single(spool.Pending());
            using var ingress = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
            ingress.DefaultRequestHeaders.Authorization = new("Bearer", settings.WriteToken);
            await LocalReporter.DeliverAsync(ingress, spool, observation with { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(),
                ItemId = "machine", Delivery = null, Heartbeat = true }, timeout.Token);
            Assert.Empty(spool.Pending());
            using var globalHistory = await client.GetAsync("items/project-evidence/events", timeout.Token);
            using var globalEvents = JsonDocument.Parse(await globalHistory.Content.ReadAsStringAsync(timeout.Token));
            Assert.Equal(2, globalEvents.RootElement.GetArrayLength());
            var budgetScope = new TaskScope("synthetic-budget-task", 1);
            var budget = observation with { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), ItemId = "synthetic-budget-item", Delivery = null,
                Control = new(budgetScope, "synthetic-budget", "budget", DateTimeOffset.UtcNow) };
            using (var accepted = await ingress.PostAsJsonAsync("api/ledger/v1/events", new[] { budget }, LedgerJson.Options, timeout.Token))
                Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            using (var status = await client.GetFromJsonAsync<JsonDocument>("status", timeout.Token))
                Assert.Equal(1, status!.RootElement.GetProperty("pausedCount").GetInt32());
            var refreshedWork = await reader.WorkAsync(timeout.Token);
            Assert.Contains(refreshedWork.Items, row => row.Item.Id == "synthetic-budget-item");
            var workingOnly = await reader.WorkAsync(timeout.Token, "working", 1);
            Assert.Empty(workingOnly.Items);
            Assert.Equal(refreshedWork.Counts, workingOnly.Counts);
            var queued = budget with { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Control = null,
                Work = new() { Scope = budgetScope, QueuedInputs = 1, OldestQueuedAtUtc = DateTimeOffset.UtcNow } };
            using (var accepted = await ingress.PostAsJsonAsync("api/ledger/v1/events", new[] { queued }, LedgerJson.Options, timeout.Token))
                Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            using (var status = await client.GetFromJsonAsync<JsonDocument>("status", timeout.Token))
                Assert.Equal(1, status!.RootElement.GetProperty("pausedCount").GetInt32());
        } finally {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await Task.WhenAll(output, error);
            Directory.Delete(directory, true);
        }
    }
}
