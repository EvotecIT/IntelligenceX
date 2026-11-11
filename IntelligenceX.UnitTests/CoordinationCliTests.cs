using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using IntelligenceX.Cli.Ledger;
using IntelligenceX.Ledger.Contracts;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class CoordinationCliTests {
    [Fact]
    public async Task CancelRequiresApplyAndPostsExactCommandFileToRequestRoute() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-cancel-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        using var listener = new HttpListener();
        var endpoint = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(endpoint);
        listener.Start();
        try {
            var config = Path.Combine(directory, "client.json");
            var command = Path.Combine(directory, "command.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new CoordinationClientSettings(endpoint, new('c', 32)),
                LedgerReadJson.Default.CoordinationClientSettings));
            await File.WriteAllTextAsync(command, "{\"expectedGeneration\":0,\"reasonRef\":\"obsolete-source\"}");
            var pending = listener.GetContextAsync();
            Assert.Equal(1, await CoordinationRunner.RunAsync(["cancel", "obsolete", command, "--config", config]));
            Assert.False(pending.IsCompleted);
            var applying = CoordinationRunner.RunAsync(["cancel", "obsolete", command, "--config", config, "--apply"]);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var context = await pending.WaitAsync(deadline.Token);
            try {
                Assert.Equal("POST", context.Request.HttpMethod);
                Assert.Equal("/api/coordination/v1/requests/obsolete/cancel", context.Request.Url!.AbsolutePath);
                Assert.Equal("Bearer " + new string('c', 32), context.Request.Headers["Authorization"]);
                using var body = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: deadline.Token);
                Assert.Equal(2, body.RootElement.EnumerateObject().Count());
                Assert.Equal(0, body.RootElement.GetProperty("expectedGeneration").GetInt64());
                Assert.Equal("obsolete-source", body.RootElement.GetProperty("reasonRef").GetString());
                // Only the transport response is synthetic. The CLI parser, command file and client run normally.
                var request = new CoordinationRequest("obsolete", "task", "build", "build", null, null, new(BuildSlots: 1),
                    new("sample", "feature/sample", new('a', 40), null, "checkpoint", "validate"), [], 1, "authorization");
                var response = new CoordinationWork(request, "requester", 1, "cancelled", DateTimeOffset.UtcNow, Reason: "obsolete-source");
                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.OutputStream, response, LedgerReadJson.Default.CoordinationWork, deadline.Token);
            } finally {
                context.Response.Close();
            }
            Assert.Equal(0, await applying.WaitAsync(deadline.Token));
        } finally {
            listener.Stop();
            Directory.Delete(directory, recursive: true);
        }
    }
}
