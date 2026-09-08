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
            : new CopilotNativeTransport(new() { GitHubToken = "host-token", HttpMessageHandler = handler, RequestTimeout = TimeSpan.FromMilliseconds(80) });
        var thread = await transport.StartThreadAsync("model", null, null, null, default);
        var turn = transport.StartTurnAsync(thread.Id, ChatInput.FromText("question"), new() { Model = "model" }, null, null, null, cancellation.Token);
        try {
            await blocked.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (route == "native") cancellation.Cancel();
            Assert.Same(turn, await Task.WhenAny(turn, Task.Delay(500)));
            if (route == "native") Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(() => turn)).CancellationToken);
            else await Assert.ThrowsAsync<TimeoutException>(() => turn);
        } finally {
            released.TrySetResult(true);
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
