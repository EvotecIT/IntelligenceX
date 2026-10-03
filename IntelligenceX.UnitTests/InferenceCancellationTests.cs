using System.Net;
using System.Net.Http.Headers;
using IntelligenceX.Copilot.Native;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Transport;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class InferenceCancellationTests {
    [Theory]
    [InlineData("copilot-chat", "body")]
    [InlineData("copilot-responses", "body")]
    [InlineData("native", "body")]
    [InlineData("copilot-chat", "stream")]
    [InlineData("copilot-responses", "stream")]
    [InlineData("native", "stream")]
    [InlineData("copilot-chat", "headers")]
    [InlineData("copilot-responses", "headers")]
    [InlineData("native", "headers")]
    public async Task StreamingInferenceBoundsNonCooperativeHttpStages(string route, string stage) {
        var blocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var body = new PendingStream(blocked, released);
        var content = stage == "stream" ? (HttpContent)new PendingContent(body, blocked, released) : new StreamContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        var handler = new Handler(async request => {
            if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK) { Content = new StringContent(route == "copilot-responses"
                ? "{\"data\":[{\"id\":\"model\",\"supported_endpoints\":[\"/responses\"]}]}"
                : "{\"data\":[{\"id\":\"model\",\"supported_endpoints\":[\"/chat/completions\"]}]}") };
            if (stage == "headers") { blocked.TrySetResult(true); await released.Task; }
            return response;
        });
        using var cancellation = new CancellationTokenSource();
        using var http = route == "native" ? new HttpClient(handler) : null;
        using IOpenAITransport transport = route == "native"
            ? new OpenAINativeTransport(new() { AuthStore = new Store(), LoadCodexAuthJson = false, PersistCodexAuthJson = false }, http!)
            : new CopilotNativeTransport(new() { GitHubToken = "host-token", HttpMessageHandler = handler, RequestTimeout = TimeSpan.FromSeconds(30) });
        var thread = await transport.StartThreadAsync("model", null, null, null, default);
        var turn = transport.StartTurnAsync(thread.Id, ChatInput.FromText("question"), new() { Model = "model" }, null, null, null, cancellation.Token);
        try {
            await blocked.Task.WaitAsync(TimeSpan.FromSeconds(3));
            cancellation.Cancel();
            // This is a deadlock guard, not a scheduler-latency assertion. The HTTP fixture
            // remains blocked until after the result, so a missing cancellation bound still fails.
            Assert.Same(turn, await Task.WhenAny(turn, Task.Delay(TimeSpan.FromSeconds(10))));
            Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(() => turn)).CancellationToken);
        } finally {
            released.TrySetResult(true);
            await Record.ExceptionAsync(() => turn);
        }
        await body.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData("/chat/completions")]
    [InlineData("/responses")]
    public async Task CopilotRequestDeadlineBoundsANonCooperativeSend(string endpoint) {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var body = new PendingStream(new(TaskCreationOptions.RunContinuationsAsynchronously), released);
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) };
        using var transport = new CopilotNativeTransport(new() {
            GitHubToken = "host-token", RequestTimeout = TimeSpan.FromSeconds(2),
            HttpMessageHandler = new Handler(request => {
                if (request.Method == HttpMethod.Get) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent("{\"data\":[{\"id\":\"model\",\"supported_endpoints\":[\"" + endpoint + "\"]}]}")
                });
                entered.TrySetResult(true);
                return lateResponse.Task;
            })
        });
        // Populate the model catalog before the deadline under test, keeping discovery/JIT cost
        // separate from proof that a blocked HTTP send is bounded.
        await transport.ListModelsAsync(default);
        var thread = await transport.StartThreadAsync("model", null, null, null, default);
        var turn = transport.StartTurnAsync(thread.Id, ChatInput.FromText("question"), new() { Model = "model" }, null, null, null, default);
        try {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Same(turn, await Task.WhenAny(turn, Task.Delay(TimeSpan.FromSeconds(10))));
            await Assert.ThrowsAsync<TimeoutException>(() => turn);
        } finally {
            released.TrySetResult(true);
            lateResponse.TrySetResult(response);
            await Record.ExceptionAsync(() => turn);
        }
        await body.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private sealed class Store : IAuthBundleStore {
        public Task<AuthBundle?> GetAsync(string provider, string? accountId = null, CancellationToken cancellationToken = default) => Task.FromResult<AuthBundle?>(new("openai-codex", "token", "", null) { AccountId = "account" });
        public Task<IReadOnlyList<AuthBundle>> ListAsync(string provider, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveAsync(AuthBundle bundle, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
    private sealed class PendingStream(TaskCompletionSource<bool> blocked, TaskCompletionSource<bool> released) : MemoryStream {
        internal readonly TaskCompletionSource<bool> Disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            blocked.TrySetResult(true); await released.Task; return 0;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
            blocked.TrySetResult(true); await released.Task; return 0;
        }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); Disposed.TrySetResult(true); }
    }
    private sealed class PendingContent(Stream stream, TaskCompletionSource<bool> blocked, TaskCompletionSource<bool> released) : HttpContent {
        protected override Task SerializeToStreamAsync(Stream target, TransportContext? context) => throw new NotSupportedException();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override async Task<Stream> CreateContentReadStreamAsync() { blocked.TrySetResult(true); await released.Task; return stream; }
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) => CreateContentReadStreamAsync();
    }
}
