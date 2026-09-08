using System.Net;
using System.Net.Sockets;
using IntelligenceX.Copilot;
using IntelligenceX.Json;
using IntelligenceX.Rpc;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class CopilotHandshakeRetryTests {
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    public async Task FailedHandshakeUsesTheConfiguredConnectionRetry(bool timeout, int retries) {
        using var peer = new HandshakePeer(timeout);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pending = CopilotClient.StartAsync(new() { CliUrl = peer.Address, AutoStart = false, UseStdio = false,
            ConnectRetryCount = retries, ConnectRetryInitialDelay = TimeSpan.Zero,
            ConnectTimeout = TimeSpan.FromMilliseconds(500) }, deadline.Token);
        if (retries == 1) {
            using var client = await pending;
            Assert.True((await client.GetAuthStatusAsync(deadline.Token)).IsAuthenticated);
        } else {
            var error = await Record.ExceptionAsync(() => pending);
            Assert.NotNull(error);
            Assert.False(deadline.IsCancellationRequested);
        }
        Assert.Equal(retries + 1, peer.Connections);
    }

    [Fact]
    public async Task CallerCancellationDuringHandshakePreventsAnotherAttempt() {
        using var peer = new HandshakePeer(true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pending = CopilotClient.StartAsync(new() { CliUrl = peer.Address, AutoStart = false, UseStdio = false,
            ConnectRetryCount = 3, ConnectRetryInitialDelay = TimeSpan.Zero, ConnectTimeout = TimeSpan.Zero }, deadline.Token);
        await peer.FirstHandshake.Task.WaitAsync(TimeSpan.FromSeconds(5));
        deadline.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, peer.Connections);
    }

    private sealed class HandshakePeer : IDisposable {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private int _connections;
        internal int Connections => Volatile.Read(ref _connections);
        internal TaskCompletionSource<bool> FirstHandshake { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string Address => "127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;

        internal HandshakePeer(bool timeout) {
            _listener.Start();
            _loop = Task.Run(async () => {
                try {
                    while (!_stop.IsCancellationRequested) {
                        using var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
                        int attempt = Interlocked.Increment(ref _connections);
                        using var transport = new HeaderDelimitedMessageTransport(socket.GetStream(), socket.GetStream());
                        await transport.ReadLoopAsync(line => {
                            var message = JsonLite.Parse(line).AsObject()!;
                            if (attempt == 1) {
                                FirstHandshake.TrySetResult(true);
                                if (!timeout) socket.Client.Shutdown(SocketShutdown.Send);
                                return;
                            }
                            var result = new JsonObject().Add("isAuthenticated", true);
                            var reply = new JsonObject().Add("jsonrpc", "2.0").Add("id", message.GetInt64("id")!.Value).Add("result", result);
                            transport.SendAsync(JsonLite.Serialize(reply), _stop.Token).GetAwaiter().GetResult();
                        }, _stop.Token);
                    }
                } catch (Exception) when (_stop.IsCancellationRequested) { }
            });
        }

        public void Dispose() {
            _stop.Cancel();
            _listener.Stop();
            _loop.GetAwaiter().GetResult();
            _stop.Dispose();
        }
    }
}
