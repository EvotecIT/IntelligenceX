using System.Net;
using System.Net.Sockets;
using IntelligenceX.Copilot;
using IntelligenceX.Json;
using IntelligenceX.Rpc;
using IntelligenceX.Treatment;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class CopilotConnectionContractsTests {
    [Fact]
    public async Task DisconnectReachesEveryAcknowledgedWaiterDespiteThrowingObservers() {
        using var peer = new Peer("{}", false, holdResponses: true);
        using var client = await CopilotClient.StartAsync(new() { CliUrl = peer.Address, AutoStart = false,
            UseStdio = false, ConnectRetryCount = 0 });
        using var first = await client.CreateSessionAsync();
        using var second = await client.CreateSessionAsync();
        using var observer1 = first.OnEvent(_ => throw new InvalidOperationException("first observer"));
        using var observer2 = second.OnEvent(_ => throw new InvalidOperationException("second observer"));
        var pending1 = first.SendAndWaitAsync(new() { Prompt = "one" });
        var pending2 = second.SendAndWaitAsync(new() { Prompt = "two" });
        await peer.TwoSendsAcknowledged.Task.WaitAsync(TimeSpan.FromSeconds(2));
        peer.Disconnect();
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending1.WaitAsync(TimeSpan.FromSeconds(2)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending2.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task InvalidExecutablePreservesItsStartupCause(int retries) {
        string path = Path.Combine(Path.GetTempPath(), "ix-invalid-cli-" + Guid.NewGuid().ToString("N") + ".exe");
        await File.WriteAllTextAsync(path, "This is not an executable.");
        try {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotClient.StartAsync(new() {
                CliPath = path, WorkingDirectory = Path.GetTempPath(), AutoInstallCli = false,
                ConnectRetryCount = retries, ConnectRetryInitialDelay = TimeSpan.Zero
            }));
            Assert.Contains("failed to start", error.Message);
            Assert.IsType<System.ComponentModel.Win32Exception>(error.InnerException);
        } finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{}", false, true)]
    [InlineData("{}", true, true)]
    [InlineData("ok", true, false)]
    public async Task ZeroTimeoutAndSmallModelBudgetSurviveMetadataAndSessionDeleteFailure(string response, bool deleteFails, bool json) {
        using var peer = new Peer(response, deleteFails);
        var options = new CopilotTreatmentProvider().CreateClientOptions(Path.GetTempPath());
        options.CliUrl = peer.Address;
        options.AutoStart = false;
        options.UseStdio = false;
        options.ConnectTimeout = TimeSpan.Zero;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = await CopilotClient.StartAsync(options, deadline.Token);
        var request = new TreatmentRequest { Prompt = "text", Model = "fixture", Ephemeral = true, MaxResponseBytes = 2 };
        TreatmentResult result = await CopilotTreatmentProvider.RunSessionAsync(client, request, "text", 2, deadline.Token);
        Assert.Equal("completed", result.Status);
        Assert.Equal(response, result.Text);
        Assert.Equal(json, result.JsonObject is not null);
        Assert.True(peer.Deleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReaderTerminationFailsLaterCallsWithoutWaitingForAResponseTimeout(bool overBudget) {
        using var peer = new Peer("{}", false);
        using var client = await CopilotClient.StartAsync(new() { CliUrl = peer.Address, AutoStart = false,
            UseStdio = false, ConnectRetryCount = 0, MaxReceivedBytes = overBudget ? 256 : 1_000_000 });
        if (overBudget) await peer.SendOversizedFrameAsync();
        else peer.Disconnect();
        var reader = (Task)typeof(CopilotClient).GetField("_readerTask", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(client)!;
        await reader.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListModelsAsync().WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private sealed class Peer : IDisposable {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private readonly string _response;
        private readonly bool _deleteFails;
        private readonly bool _holdResponses;
        private int _sessions;
        private int _sends;
        internal TaskCompletionSource<bool> TwoSendsAcknowledged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TcpClient? _connection;
        private HeaderDelimitedMessageTransport? _transport;
        internal bool Deleted { get; private set; }
        internal string Address => "127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;

        internal Peer(string response, bool deleteFails, bool holdResponses = false) {
            _response = response; _deleteFails = deleteFails; _holdResponses = holdResponses; _listener.Start();
            _loop = Task.Run(async () => {
                try {
                    using var connection = _connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                    using var transport = _transport = new HeaderDelimitedMessageTransport(connection.GetStream(), connection.GetStream());
                    await transport.ReadLoopAsync(Handle, _stop.Token);
                } catch (Exception) when (_stop.IsCancellationRequested) { }
            });
        }

        private void Handle(string line) {
            JsonObject message = JsonLite.Parse(line).AsObject()!;
            string? method = message.GetString("method");
            var result = new JsonObject();
            if (method == "auth.getStatus") result.Add("isAuthenticated", true).Add("metadata", new string('x', 4096));
            if (method == "session.create") result.Add("sessionId", "s" + ++_sessions);
            if (method == "session.send") {
                result.Add("messageId", "m1");
                if (!_holdResponses) {
                    SendEvent("assistant.message", new JsonObject().Add("content", _response));
                    SendEvent("session.idle", new JsonObject());
                }
            }
            if (method == "session.delete") Deleted = true;
            var reply = new JsonObject().Add("jsonrpc", "2.0").Add("id", message.GetInt64("id")!.Value);
            if (method == "session.delete" && _deleteFails) reply.Add("error", new JsonObject().Add("code", -1).Add("message", "cleanup failed"));
            else reply.Add("result", result);
            Send(reply);
            if (method == "session.send" && ++_sends == 2) TwoSendsAcknowledged.TrySetResult(true);
        }

        private void SendEvent(string type, JsonObject data) => Send(new JsonObject().Add("jsonrpc", "2.0")
            .Add("method", "session.event").Add("params", new JsonObject().Add("sessionId", "s1")
                .Add("event", new JsonObject().Add("type", type).Add("data", data))));
        private void Send(JsonObject message) => _transport!.SendAsync(JsonLite.Serialize(message), _stop.Token).GetAwaiter().GetResult();
        internal Task SendOversizedFrameAsync() => _transport!.SendAsync(JsonLite.Serialize(new JsonObject().Add("data", new string('x', 1000))), _stop.Token);
        internal void Disconnect() => _connection!.Client.Shutdown(SocketShutdown.Send);

        public void Dispose() {
            _stop.Cancel(); _connection?.Dispose(); _listener.Stop();
            try { _loop.GetAwaiter().GetResult(); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            _stop.Dispose();
        }
    }
}
