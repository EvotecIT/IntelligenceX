using System.Net;
using System.Net.Sockets;
using IntelligenceX.Copilot;
using IntelligenceX.Json;
using IntelligenceX.Rpc;
using IntelligenceX.Treatment;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class CopilotConnectionContractsTests {
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
        private TcpClient? _connection;
        private HeaderDelimitedMessageTransport? _transport;
        internal bool Deleted { get; private set; }
        internal string Address => "127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;

        internal Peer(string response, bool deleteFails) {
            _response = response; _deleteFails = deleteFails; _listener.Start();
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
            if (method == "session.create") result.Add("sessionId", "s1");
            if (method == "session.send") {
                result.Add("messageId", "m1");
                SendEvent("assistant.message", new JsonObject().Add("content", _response));
                SendEvent("session.idle", new JsonObject());
            }
            if (method == "session.delete") Deleted = true;
            var reply = new JsonObject().Add("jsonrpc", "2.0").Add("id", message.GetInt64("id")!.Value);
            if (method == "session.delete" && _deleteFails) reply.Add("error", new JsonObject().Add("code", -1).Add("message", "cleanup failed"));
            else reply.Add("result", result);
            Send(reply);
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
