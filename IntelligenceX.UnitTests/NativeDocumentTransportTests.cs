using System.Net;
using System.Text;
using System.Text.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.CompatibleHttp;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Transport;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class NativeDocumentTransportTests {
    [Theory]
    [InlineData("native", "success")]
    [InlineData("native", "failure")]
    [InlineData("native", "cancellation")]
    [InlineData("compatible-json", "success")]
    [InlineData("compatible-json", "failure")]
    [InlineData("compatible-json", "cancellation")]
    public async Task ThrowingCompletionObserversDoNotChangeTheOperationOutcome(string route, string outcome) {
        using var cancellation = new CancellationTokenSource();
        var cause = new IOException("provider failure");
        using var http = new HttpClient(new Handler(_ => {
            if (outcome == "failure") throw cause;
            if (outcome == "cancellation") { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(route == "native"
                ? Event(new { type = "response.completed", response = new { status = "completed", output = Array.Empty<object>() } })
                : "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"answer\"}}]}") });
        }));
        using var client = new IntelligenceXClient(CreateTransport(route, http), "model", null, null, null);
        var turns = new List<IntelligenceX.Telemetry.IntelligenceXTurnCompletedEventArgs>();
        var calls = new List<IntelligenceX.Telemetry.RpcCallCompletedEventArgs>();
        client.TurnCompleted += (_, _) => throw new InvalidOperationException("turn observer");
        client.TurnCompleted += (_, args) => turns.Add(args);
        client.RpcCallCompleted += (_, _) => throw new InvalidOperationException("rpc observer");
        client.RpcCallCompleted += (_, args) => calls.Add(args);
        Task Run() => client.ChatAsync(ChatInput.FromText("document"), new ChatOptions { Ephemeral = true }, cancellation.Token);
        if (outcome == "success") await Run();
        else if (outcome == "failure") Assert.Same(cause, await Assert.ThrowsAsync<IOException>(Run));
        else Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(Run)).CancellationToken);
        Assert.Equal(outcome == "success", Assert.Single(turns).Success);
        Assert.Equal(outcome == "success", Assert.Single(calls).Success);
    }

    [Fact]
    public async Task ConcurrentConversationCreationLookupAndCleanupKeepIndependentState() {
        var store = new OpenAINativeThreadStore();
        await Task.WhenAll(Enumerable.Range(0, 16).Select(worker => Task.Run(() => {
            for (int i = 0; i < 2000; i++) {
                var state = store.StartNew("model");
                Assert.True(store.TryGet(state.Id, out var read));
                Assert.Same(state, read);
                Assert.Same(state, store.Resume(state.Id, "model"));
                store.Forget(state.Id);
                Assert.False(store.TryGet(state.Id, out _));
            }
        })));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("transport-failure")]
    [InlineData("input-validation")]
    [InlineData("cancellation")]
    public async Task EphemeralTreatmentPreservesTheSelectedConversationOnEveryExit(string outcome) {
        var requests = new List<string>();
        using var http = new HttpClient(new Handler(async request => {
            string body = await request.Content!.ReadAsStringAsync();
            requests.Add(body);
            if (body.Contains("temporary-document") && outcome == "transport-failure") throw new IOException("fixture failure");
            return new(HttpStatusCode.OK) { Content = new StringContent(Event(new {
                type = "response.completed", response = new { status = "completed", output = Array.Empty<object>() }
            })) };
        }));
        using var client = new IntelligenceXClient(new OpenAINativeTransport(Options(), http), "model", null, null, null);
        var threads = new List<string>();
        client.TurnCompleted += (_, value) => threads.Add(value.ThreadId);
        await client.ChatAsync("established-conversation");
        var input = ChatInput.FromText("temporary-document");
        if (outcome == "input-validation") input.AddImagePath(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".missing.png"));
        using var cancellation = new CancellationTokenSource();
        if (outcome == "cancellation") cancellation.Cancel();
        Task Run() => client.ChatAsync(input, new ChatOptions { Ephemeral = true }, cancellation.Token);
        if (outcome == "success") await Run();
        else if (outcome == "input-validation") await Assert.ThrowsAsync<FileNotFoundException>(Run);
        else if (outcome == "cancellation") await Assert.ThrowsAnyAsync<OperationCanceledException>(Run);
        else await Assert.ThrowsAsync<IOException>(Run);
        await client.ChatAsync("continue-conversation");
        Assert.Equal(threads[0], threads[^1]);
        Assert.Contains("established-conversation", requests[^1]);
        Assert.DoesNotContain("temporary-document", requests[^1]);
    }

    [Fact]
    public async Task SelectingAConversationDuringEphemeralWorkDoesNotChangeTurnIdentity() {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async request => {
            if ((await request.Content!.ReadAsStringAsync()).Contains("temporary-document")) {
                entered.TrySetResult(true); await release.Task;
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(Event(new {
                type = "response.completed", response = new { status = "completed", output = Array.Empty<object>() }
            })) };
        }));
        using var client = new IntelligenceXClient(new OpenAINativeTransport(Options(), http), "model", null, null, null);
        var threads = new List<string>();
        client.TurnCompleted += (_, value) => threads.Add(value.ThreadId);
        await client.ChatAsync("original");
        var temporary = client.ChatAsync(ChatInput.FromText("temporary-document"), new ChatOptions { Ephemeral = true, NewThread = true });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var selected = await client.StartNewThreadAsync();
        release.TrySetResult(true);
        await temporary;
        Assert.NotEqual(selected.Id, threads[^1]);
        await client.ChatAsync("new-selected-conversation");
        Assert.Equal(selected.Id, threads[^1]);
    }

    [Theory]
    [InlineData("native", false, "disposed")]
    [InlineData("native", false, "io")]
    [InlineData("native", false, "eof")]
    [InlineData("native", true, "disposed")]
    [InlineData("native", true, "io")]
    [InlineData("native", true, "eof")]
    [InlineData("compatible-stream", false, "disposed")]
    [InlineData("compatible-stream", false, "io")]
    [InlineData("compatible-stream", false, "eof")]
    [InlineData("compatible-json", false, "disposed")]
    [InlineData("compatible-json", false, "io")]
    [InlineData("compatible-json", false, "eof")]
    public async Task AbortedProviderResponseReadsPreserveCallerCancellation(string route, bool errorResponse, string abortKind) {
        using var stream = new AbortedReadStream(abortKind);
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(errorResponse ? HttpStatusCode.BadRequest : HttpStatusCode.OK) {
            Content = new StreamContent(stream)
        })));
        using IOpenAITransport transport = CreateTransport(route, http);
        var thread = await transport.StartThreadAsync("model", null, null, null, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var pending = transport.StartTurnAsync(thread.Id, ChatInput.FromText("read"), new ChatOptions { Model = "model" },
            null, null, null, cancellation.Token);
        await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Theory]
    [InlineData("native")]
    [InlineData("compatible-stream")]
    [InlineData("compatible-json")]
    public async Task ReadFailureWithoutCancellationRetainsItsTransportCause(string route) {
        using var stream = new AbortedReadStream("io");
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) })));
        using var transport = CreateTransport(route, http);
        var thread = await transport.StartThreadAsync("model", null, null, null, CancellationToken.None);
        var pending = transport.StartTurnAsync(thread.Id, ChatInput.FromText("read"), new ChatOptions { Model = "model" },
            null, null, null, CancellationToken.None);
        await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        stream.Fail();
        var error = await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("fixture transport failure", error.Message);
    }

    private static IOpenAITransport CreateTransport(string route, HttpClient http) => route == "native"
        ? new OpenAINativeTransport(Options(), http)
        : new OpenAICompatibleHttpTransport(new() { BaseUrl = "http://127.0.0.1:11434", AllowInsecureHttp = true, Streaming = route == "compatible-stream" }, http);

    private sealed class AbortedReadStream(string abortKind) : Stream {
        internal TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<int> _read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Fail() => _read.TrySetException(new IOException("fixture transport failure"));
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            Entered.TrySetResult(true); return _read.Task;
        }
        protected override void Dispose(bool disposing) {
            if (abortKind == "eof") _read.TrySetResult(0);
            else _read.TrySetException(abortKind == "io" ? new IOException("aborted read") : new ObjectDisposedException("aborted read"));
            base.Dispose(disposing);
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task SchemaInlineImageAndWhitespaceSurviveNativeTransport() {
        string body = string.Empty;
        using var http = new HttpClient(new Handler(async request => {
            body = await request.Content!.ReadAsStringAsync();
            string events = Event(new { type = "response.output_text.delta", delta = "Total:" })
                + Event(new { type = "response.output_text.delta", delta = " " })
                + Event(new { type = "response.output_text.delta", delta = "42" })
                + Event(new { type = "response.completed", response = new { id = "result", status = "completed", output = Array.Empty<object>() } });
            return new(HttpStatusCode.OK) { Content = new StringContent(events, Encoding.UTF8, "text/event-stream") };
        }));
        using var transport = new OpenAINativeTransport(Options(), http);
        var thread = await transport.StartThreadAsync("vision-model", null, null, null, CancellationToken.None);
        var input = ChatInput.FromText("Extract this image");
        input.AddImageBytes(new byte[] { 1, 2, 3 }, "image/png");
        var turn = await transport.StartTurnAsync(thread.Id, input, new ChatOptions {
            Model = "vision-model", MaxResponseBytes = 4096,
            ResponseFormat = new("document", "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}")
        }, null, null, null, CancellationToken.None);
        Assert.Equal("Total: 42", EasyChatResult.FromTurn(turn).Text);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("json_schema", json.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        Assert.Contains("data:image/png;base64,AQID", body);
        Assert.False(json.RootElement.TryGetProperty("tools", out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task NativeResponseBudgetCoversSuccessAndErrorBodies(HttpStatusCode status) {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(status) {
            Content = new StringContent(new string('x', 5000))
        })));
        using var transport = new OpenAINativeTransport(Options(), http);
        var thread = await transport.StartThreadAsync("model", null, null, null, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(() => transport.StartTurnAsync(thread.Id, ChatInput.FromText("hello"),
            new ChatOptions { Model = "model", MaxResponseBytes = 1024 }, null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task DisabledModelFallbackSendsOnlyOneRequest() {
        int calls = 0;
        using var http = new HttpClient(new Handler(_ => {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) {
                Content = new StringContent("{\"error\":{\"message\":\"The model is not supported when using Codex with a ChatGPT account.\"}}")
            });
        }));
        using var transport = new OpenAINativeTransport(Options(), http);
        var thread = await transport.StartThreadAsync("unsupported-model", null, null, null, CancellationToken.None);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => transport.StartTurnAsync(thread.Id, ChatInput.FromText("hello"),
            new ChatOptions { Model = "unsupported-model" }, null, null, null, CancellationToken.None));
        Assert.Equal(1, calls);
    }

    private static string Event(object value) => "data: " + JsonSerializer.Serialize(value) + "\n\n";
    [Fact]
    public async Task EphemeralClientForgetsSourceEvenIfItsOldThreadIdIsResumed() {
        var requests = new List<string>();
        using var http = new HttpClient(new Handler(async request => {
            requests.Add(await request.Content!.ReadAsStringAsync());
            return new(HttpStatusCode.OK) { Content = new StringContent(Event(new {
                type = "response.completed", response = new { status = "completed", output = new[] { new {
                    type = "message", role = "assistant", content = new[] { new { type = "output_text", text = "answer" } }
                } } }
            })) };
        }));
        var transport = new OpenAINativeTransport(Options(), http);
        using var client = new IntelligenceXClient(transport, "model", null, null, null);
        string? priorThread = null;
        client.TurnCompleted += (_, value) => priorThread = value.ThreadId;
        await client.ChatAsync(ChatInput.FromText("sensitive-first-document"), new ChatOptions { Ephemeral = true });
        Assert.NotNull(priorThread);
        await client.UseThreadAsync(priorThread!);
        await client.ChatAsync(ChatInput.FromText("second-document"));
        Assert.Equal(2, requests.Count);
        Assert.DoesNotContain("sensitive-first-document", requests[1]);
        Assert.Contains("second-document", requests[1]);
    }

    private static OpenAINativeOptions Options() => new() {
        AuthStore = new Store(), LoadCodexAuthJson = false, PersistCodexAuthJson = false,
        EnableModelFallback = false, EnableToolSchemaFallback = false, AllowSensitiveDiagnostics = false,
        ImageGeneration = new() { Enabled = false }
    };
    private sealed class Store : IRemovableAuthBundleStore {
        private readonly AuthBundle _bundle = new("openai-codex", "test-token", "", DateTimeOffset.UtcNow.AddHours(1)) { AccountId = "test-account" };
        public Task<AuthBundle?> GetAsync(string provider, string? accountId = null, CancellationToken cancellationToken = default) => Task.FromResult<AuthBundle?>(_bundle);
        public Task<IReadOnlyList<AuthBundle>> ListAsync(string provider, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuthBundle>>(new[] { _bundle });
        public Task RemoveAsync(string provider, string? accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException("Logout is outside this fixture contract.");
        public Task SaveAsync(AuthBundle bundle, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Fixture must not refresh credentials.");
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
