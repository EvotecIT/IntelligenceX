using System.Reflection;
using System.Text;
using IntelligenceX.Copilot;
using IntelligenceX.Json;
using IntelligenceX.Rpc;
using IntelligenceX.Treatment;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class CopilotDocumentContractsTests {
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

    [Theory]
    [InlineData(false, 4, true)]
    [InlineData(false, 3, false)]
    [InlineData(true, 4, true)]
    [InlineData(true, 3, false)]
    public async Task ModelByteLimitCountsUtf8AndSurrogatesAcrossStreamingDeltas(bool streaming, int maximum, bool succeeds) {
        using var client = Client(_ => new JsonObject().Add("messageId", "m1"));
        using var session = new CopilotSession("test-session", client);
        Task<string?> pending = session.SendAndWaitAsync(new() { Prompt = "text", MaxResponseBytes = maximum });
        if (streaming) {
            session.Dispatch(Event("assistant.message_delta", "deltaContent", "\ud83d"));
            session.Dispatch(Event("assistant.message_delta", "deltaContent", "\ude00"));
        } else session.Dispatch(Event("assistant.message", "content", "😀"));
        session.Dispatch(Event("session.idle", "unused", ""));
        if (succeeds) Assert.Equal("😀", await pending);
        else await Assert.ThrowsAsync<InvalidDataException>(() => pending);
    }

    [Fact]
    public async Task NullInputIsRejectedBySharedValidationBeforeStartingCli() {
        var request = new TreatmentRequest { Prompt = "text", Model = "model", Ephemeral = true,
            Inputs = new TreatmentInputArtifact[] { null! } };
        Assert.Throws<ArgumentException>(() => TreatmentPromptBuilder.Validate(request));
        await Assert.ThrowsAsync<ArgumentException>(() => new CopilotTreatmentProvider("missing-cli").RunAsync(request));
    }

    [Fact]
    public void RuntimeCleanupRemovesOwnedFilesAndToleratesIoFailure() {
        string runtime = Path.Combine(Path.GetTempPath(), "intelligencex-treatment-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runtime);
        try {
            File.WriteAllText(Path.Combine(runtime, "cli.log"), "test");
            CopilotTreatmentProvider.CleanupRuntimeDirectory(runtime);
            Assert.False(Directory.Exists(runtime));
            CopilotTreatmentProvider.CleanupRuntimeDirectory(runtime);
            // A file where the directory was expected makes Directory.Delete fail on every platform.
            File.WriteAllText(runtime, "unremovable as directory");
            Assert.Null(Record.Exception(() => CopilotTreatmentProvider.CleanupRuntimeDirectory(runtime)));
            Assert.True(File.Exists(runtime));
        } finally {
            if (File.Exists(runtime)) File.Delete(runtime);
            if (Directory.Exists(runtime)) Directory.Delete(runtime, recursive: true);
        }
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
    public async Task ProtocolVersionIsExplicitForCopilotWithoutChangingLegacyRpc(bool versioned) {
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

    [Fact]
    public async Task RestrictedSessionUsesObjectParametersAndClosesAmbientCapabilities() {
        JsonObject? captured = null;
        using var client = Client(request => {
            captured = request.GetObject("params");
            return new JsonObject().Add("sessionId", "test-session");
        });
        using var session = await client.CreateSessionAsync(new() { Model = "model", Restricted = true, SystemMessage = "instructions" });
        Assert.NotNull(captured);
        Assert.Equal(0, captured!.GetArray("availableTools")!.Count);
        Assert.Equal("replace", captured.GetObject("systemMessage")!.GetString("mode"));
        foreach (string flag in new[] { "enableConfigDiscovery", "enableFileHooks", "enableHostGitOperations", "enableSessionStore", "enableSkills" })
            Assert.False(captured.GetBoolean(flag));
        Assert.False(captured.GetObject("memory")!.GetBoolean("enabled"));
    }

    [Fact]
    public async Task SessionPreservesWhitespaceDeltasAndHonorsResponseLimit() {
        using var client = Client(_ => new JsonObject().Add("messageId", "m1"));
        using var session = new CopilotSession("test-session", client);
        Task<string?> pending = session.SendAndWaitAsync(new() { Prompt = "text", MaxResponseCharacters = 10 });
        session.Dispatch(Event("assistant.message_delta", "deltaContent", "A"));
        session.Dispatch(Event("assistant.message_delta", "deltaContent", " "));
        session.Dispatch(Event("assistant.message_delta", "deltaContent", "B"));
        session.Dispatch(Event("session.idle", "unused", ""));
        Assert.Equal("A B", await pending);
        pending = session.SendAndWaitAsync(new() { Prompt = "text", MaxResponseCharacters = 2 });
        session.Dispatch(Event("assistant.message_delta", "deltaContent", "abc"));
        await Assert.ThrowsAsync<InvalidDataException>(() => pending);
    }

    [Fact]
    public async Task SessionCancellationKeepsCancellationSemantics() {
        using var client = Client(_ => new JsonObject().Add("messageId", "m1"));
        using var session = new CopilotSession("test-session", client);
        using var cancellation = new CancellationTokenSource();
        Task<string?> pending = session.SendAndWaitAsync(new() { Prompt = "text" }, cancellationToken: cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task TreatmentRejectsFileAndToolAuthorityBeforeStartingCli() {
        var provider = new CopilotTreatmentProvider("missing-cli");
        foreach (var request in new[] {
            new TreatmentRequest { Prompt = "text", Model = "model", Ephemeral = true, AllowNetwork = true },
            new TreatmentRequest { Prompt = "text", Model = "model", Ephemeral = true, Inputs = new[] { new TreatmentInputArtifact { Path = "private.txt" } } },
            new TreatmentRequest { Prompt = "text", Model = "model", Ephemeral = false }
        }) await Assert.ThrowsAsync<NotSupportedException>(() => provider.RunAsync(request));
    }

    private static CopilotSessionEvent Event(string type, string name, string value) => CopilotSessionEvent.FromJson(
        new JsonObject().Add("type", type).Add("data", new JsonObject().Add(name, value)));

    private static CopilotClient Client(Func<JsonObject, JsonObject> response) {
        var client = (CopilotClient)Activator.CreateInstance(typeof(CopilotClient), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { new CopilotClientOptions() }, null)!;
        JsonRpcClient? rpc = null;
        rpc = new JsonRpcClient(line => {
            JsonObject request = JsonLite.Parse(line)!.AsObject()!;
            rpc!.HandleLine(JsonLite.Serialize(new JsonObject().Add("id", request.GetInt64("id")!.Value).Add("result", response(request))));
            return Task.CompletedTask;
        }, true);
        typeof(CopilotClient).GetField("_rpc", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, rpc);
        return client;
    }
}
