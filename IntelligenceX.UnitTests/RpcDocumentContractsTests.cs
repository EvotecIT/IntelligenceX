using System.Reflection;
using System.Text;
using IntelligenceX.Copilot;
using IntelligenceX.Json;
using IntelligenceX.Rpc;
using IntelligenceX.Treatment;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class RpcDocumentContractsTests {
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancellation")]
    public async Task RpcCompletionObserversCannotReplaceTheResultOrOriginalError(string outcome) {
        using var cancellation = new CancellationTokenSource();
        var cause = new IOException("connection failed");
        JsonRpcClient? rpc = null;
        using var owner = rpc = new JsonRpcClient(line => {
            if (outcome == "failure") throw cause;
            if (outcome == "cancellation") { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
            var request = JsonLite.Parse(line).AsObject()!;
            rpc!.HandleLine(JsonLite.Serialize(new JsonObject().Add("id", request.GetInt64("id")!.Value).Add("result", new JsonObject().Add("value", "ok"))));
            return Task.CompletedTask;
        });
        var completed = new List<IntelligenceX.Telemetry.RpcCallCompletedEventArgs>();
        rpc.CallCompleted += (_, _) => throw new InvalidOperationException("observer");
        rpc.CallCompleted += (_, args) => completed.Add(args);
        Task<JsonValue?> Run() => rpc.CallAsync("fixture", new JsonObject(), cancellation.Token);
        if (outcome == "success") Assert.Equal("ok", (await Run())!.AsObject()!.GetString("value"));
        else if (outcome == "failure") Assert.Same(cause, await Assert.ThrowsAsync<IOException>(Run));
        else Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(Run)).CancellationToken);
        Assert.Equal(outcome == "success", Assert.Single(completed).Success);
    }

    [Fact]
    public async Task TerminalConnectionFailureRejectsPendingQueuedAndFutureRpcCalls() {
        int writes = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var rpc = new JsonRpcClient(_ => { Interlocked.Increment(ref writes); return release.Task; });
        Task<JsonValue?> pending = rpc.CallAsync("pending", new JsonObject());
        Task<JsonValue?> queued = rpc.CallAsync("queued", new JsonObject());
        try {
            rpc.FailConnection(new EndOfStreamException("closed"));
            await Assert.ThrowsAsync<EndOfStreamException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
            await Assert.ThrowsAsync<EndOfStreamException>(() => queued.WaitAsync(TimeSpan.FromSeconds(2)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => rpc.CallAsync("future", new JsonObject()).WaitAsync(TimeSpan.FromSeconds(2)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => rpc.NotifyAsync("future", new JsonObject()));
            Assert.Equal(1, writes);
        } finally { release.SetResult(); }
        using var raced = new JsonRpcClient(_ => throw new InvalidOperationException("Must not send."));
        raced.CallStarted += (_, _) => raced.FailConnection(new EndOfStreamException("closed during registration"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => raced.CallAsync("race", new JsonObject()).WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task CancellationEscapesBlockedSendAndDoesNotTransmitCanceledQueuedRequest() {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int sent = 0;
        using var rpc = new JsonRpcClient(_ => Interlocked.Increment(ref sent) == 1 ? release.Task : Task.CompletedTask);
        using var first = new CancellationTokenSource();
        using var queued = new CancellationTokenSource();
        Task<JsonValue?> active = rpc.CallAsync("first", new JsonObject(), first.Token);
        Task<JsonValue?> waiting = rpc.CallAsync("queued", new JsonObject(), queued.Token);
        first.Cancel(); queued.Cancel();
        try {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active.WaitAsync(TimeSpan.FromSeconds(2)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(1, sent);
        } finally { release.TrySetResult(); }
        await rpc.NotifyAsync("flush", new JsonObject()).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, sent);
    }

    [Fact]
    public async Task CancellationReachesCooperativeWriterAndResponseWait() {
        CancellationToken observed = default;
        using var rpc = new JsonRpcClient(async (_, token) => { observed = token; await Task.Delay(Timeout.Infinite, token); });
        using var cancellation = new CancellationTokenSource();
        Task<JsonValue?> pending = rpc.CallAsync("send", new JsonObject(), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(observed.IsCancellationRequested);
        using var sentRpc = new JsonRpcClient(_ => Task.CompletedTask);
        using var responseCancellation = new CancellationTokenSource();
        pending = sentRpc.CallAsync("response", new JsonObject(), responseCancellation.Token);
        responseCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sentRpc.CallAsync("pre-canceled", new JsonObject(), responseCancellation.Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProtocolVersionCanBeSelectedWithoutChangingLegacyRpc(bool versioned) {
        JsonRpcClient? rpc = null;
        using var instance = rpc = new JsonRpcClient(line => {
            JsonObject request = JsonLite.Parse(line)!.AsObject()!;
            Assert.Equal(versioned ? "2.0" : null, request.GetString("jsonrpc"));
            rpc!.HandleLine("{\"jsonrpc\":\"2.0\",\"id\":" + request.GetInt64("id") + ",\"result\":{\"ok\":true}}");
            return Task.CompletedTask;
        }, versioned);
        Assert.True((await rpc.CallAsync("connect", new JsonObject()))!.AsObject()!.GetBoolean("ok"));
    }

    [Fact]
    public async Task OversizedRpcFrameIsRejectedBeforeReadingOrAllocatingPayload() {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes("Content-Length: 2147483647\r\n\r\n"));
        using var output = new MemoryStream();
        using var transport = new HeaderDelimitedMessageTransport(input, output, 4096);
        await Assert.ThrowsAsync<InvalidDataException>(() => transport.ReadLoopAsync(_ => Assert.Fail("Frame must not be dispatched."), CancellationToken.None));
    }
}
