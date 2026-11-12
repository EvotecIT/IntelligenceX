using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using IntelligenceX.Cli.Ledger;
using IntelligenceX.Ledger.Contracts;
using Xunit;

namespace IntelligenceX.UnitTests;

[Collection("Coordination CLI console")]
public sealed class CoordinationRequesterCliTests {
    [Theory]
    [InlineData("checkpoint", "command.json")]
    [InlineData("policy-set", "command.json")]
    [InlineData("accept", "request-id", "result-ref")]
    [InlineData("cancel", "request-id", "command.json")]
    [InlineData("host", "command.json")]
    [InlineData("worker", "command.json")]
    [InlineData("requester-enroll", "command.json")]
    [InlineData("receipt", "request-id", "started", "command.json")]
    [InlineData("bind", "command.json")]
    [InlineData("retire", "command.json")]
    [InlineData("delivery-begin", "request-id", "command.json")]
    [InlineData("delivery-receipt", "request-id", "command.json")]
    [InlineData("consume", "request-id", "command.json")]
    [InlineData("reconcile", "command.json")]
    [InlineData("reauthorize", "command.json")]
    [InlineData("result-publish", "request-id", "command.json")]
    public async Task MutatingCommandsWithoutApplyAreRejectedBeforeSending(string verb, params string[] arguments) {
        var directory = Path.Combine(Path.GetTempPath(), "ix-apply-required-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var endpoint = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(endpoint);
        listener.Start();
        try {
            var config = Path.Combine(directory, "client.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new CoordinationClientSettings(endpoint, new('h', 32)),
                LedgerReadJson.Default.CoordinationClientSettings));
            var pending = listener.GetContextAsync();
            var result = await RunCapturedAsync([verb, .. arguments, "--config", config]);
            Assert.Equal(1, result.ExitCode);
            Assert.False(pending.IsCompleted);
            using var output = JsonDocument.Parse(result.Output);
            Assert.Equal("rejected", output.RootElement.GetProperty("outcome").GetString());
            Assert.Equal("apply-required", output.RootElement.GetProperty("error").GetString());
            Assert.True(string.IsNullOrWhiteSpace(result.Error));
        } finally {
            listener.Stop();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("{\"worker\":null}", 0)]
    [InlineData("{}", 1)]
    public async Task WorkerStatusReadsExplicitAbsenceAndRejectsMissingObservation(string response, int expectedExit) {
        var directory = Path.Combine(Path.GetTempPath(), "ix-worker-status-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var endpoint = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(endpoint);
        listener.Start();
        try {
            var config = Path.Combine(directory, "host.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new CoordinationClientSettings(endpoint, new('h', 32)),
                LedgerReadJson.Default.CoordinationClientSettings));
            var running = CoordinationRunner.RunAsync(["worker-status", "provider-a", "session-a", "--config", config]);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var context = await listener.GetContextAsync().WaitAsync(deadline.Token);
            try {
                Assert.Equal("GET", context.Request.HttpMethod);
                Assert.Equal("/api/coordination/v1/workers/provider-a/session-a", context.Request.Url!.AbsolutePath);
                context.Response.ContentType = "application/json";
                var bytes = System.Text.Encoding.UTF8.GetBytes(response);
                await context.Response.OutputStream.WriteAsync(bytes, deadline.Token);
            } finally { context.Response.Close(); }
            Assert.Equal(expectedExit, await running.WaitAsync(deadline.Token));
        } finally {
            listener.Stop();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequesterEnrollPostsHashOnlyContractThroughTypedClient(bool atomicWorkerObservation) {
        var directory = Path.Combine(Path.GetTempPath(), "ix-requester-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var endpoint = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(endpoint);
        listener.Start();
        try {
            var config = Path.Combine(directory, "client.json");
            var command = Path.Combine(directory, "enrollment.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new CoordinationClientSettings(endpoint, new('h', 32)),
                LedgerReadJson.Default.CoordinationClientSettings));
            var enrollment = new RequesterEnrollment("requester", "worker", "goal", "source", "authorization",
                [new("sample", "feature/sample")], new('a', 64), "boot", DateTimeOffset.UtcNow, false);
            if (atomicWorkerObservation) enrollment = enrollment with { WorkerObservation = new("worker", "machine", "provider",
                false, "native-turn-held", enrollment.ObservedAtUtc, "session", "workspace", BootId: "boot") };
            await File.WriteAllTextAsync(command, JsonSerializer.Serialize(enrollment, LedgerReadJson.Default.RequesterEnrollment));
            var pending = listener.GetContextAsync();
            var applying = CoordinationRunner.RunAsync(["requester-enroll", command, "--config", config, "--apply"]);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var context = await pending.WaitAsync(deadline.Token);
            try {
                Assert.Equal("POST", context.Request.HttpMethod);
                Assert.Equal("/api/coordination/v1/requesters", context.Request.Url!.AbsolutePath);
                Assert.Equal("Bearer " + new string('h', 32), context.Request.Headers["Authorization"]);
                using var body = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: deadline.Token);
                Assert.Equal(atomicWorkerObservation ? 11 : 10, body.RootElement.EnumerateObject().Count());
                Assert.Equal(enrollment.Id, body.RootElement.GetProperty("id").GetString());
                Assert.Equal(enrollment.TokenSha256, body.RootElement.GetProperty("tokenSha256").GetString());
                Assert.Equal("sample", body.RootElement.GetProperty("sources")[0].GetProperty("project").GetString());
                Assert.False(body.RootElement.GetProperty("dispatchAllowed").GetBoolean());
                if (atomicWorkerObservation) Assert.Equal("worker", body.RootElement.GetProperty("workerObservation").GetProperty("id").GetString());
                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.OutputStream, enrollment,
                    LedgerReadJson.Default.RequesterEnrollment, deadline.Token);
            } finally { context.Response.Close(); }
            Assert.Equal(0, await applying.WaitAsync(deadline.Token));
        } finally {
            listener.Stop();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCapturedAsync(string[] args) {
        var previousOutput = Console.Out;
        var previousError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        try {
            var exitCode = await CoordinationRunner.RunAsync(args);
            return (exitCode, output.ToString(), error.ToString());
        } finally {
            Console.SetOut(previousOutput);
            Console.SetError(previousError);
        }
    }
}

[CollectionDefinition("Coordination CLI console", DisableParallelization = true)]
public sealed class CoordinationCliConsoleCollection { }
