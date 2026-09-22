using System.Net;
using IntelligenceX.Copilot.Native;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class CopilotDeviceTimeoutTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonCooperativeHttpHeadersAreBoundedAndLateResponsesAreDisposed(bool cancel) {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new AsyncHandler(() => { started.SetResult(true); return released.Task; }));
        using var auth = new CopilotNativeAuthentication(new() { GitHubClientId = "app", UseEnvironmentCredentials = false,
            RequestTimeout = cancel ? TimeSpan.FromMinutes(1) : TimeSpan.FromMilliseconds(60) }, http);
        using var cancellation = new CancellationTokenSource();
        Task login = auth.LoginAsync(_ => throw new Xunit.Sdk.XunitException("Timed-out HTTP must not start sign-in."), cancellation.Token);
        var content = new TrackedContent();
        try {
            await started.Task;
            if (cancel) cancellation.Cancel();
            if (cancel) Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(() => login.WaitAsync(TimeSpan.FromSeconds(3)))).CancellationToken);
            else await Assert.ThrowsAsync<TimeoutException>(() => login.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.True(login.IsCompleted);
        } finally { released.SetResult(new(HttpStatusCode.OK) { Content = content }); }
        await content.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfiguredTimeoutBoundsEachDeviceHttpBodyWithoutLimitingHumanApproval(bool tokenPhase) {
        var bodyStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bodyReleased = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(request => {
            bool code = request.RequestUri!.AbsolutePath == "/login/device/code";
            if (code && tokenPhase) return Json(Code);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new PendingStream(bodyStarted, bodyReleased)) };
        }));
        using var auth = new CopilotNativeAuthentication(new() { GitHubClientId = "app", UseEnvironmentCredentials = false,
            RequestTimeout = TimeSpan.FromMilliseconds(60) }, http);
        int codes = 0;
        Task login = auth.LoginAsync(_ => codes++);
        try {
            await bodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Assert.ThrowsAsync<TimeoutException>(() => login.WaitAsync(TimeSpan.FromSeconds(10)));
            // The outer watchdog must not substitute for the configured provider deadline.
            Assert.True(login.IsCompleted);
            Assert.Equal(tokenPhase ? 1 : 0, codes);
        } finally {
            bodyReleased.TrySetResult(0);
            await Record.ExceptionAsync(() => login);
        }
    }

    private const string Code = "{\"device_code\":\"private\",\"user_code\":\"public\",\"verification_uri\":\"https://github.com/login/device\",\"expires_in\":60,\"interval\":1}";
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
    private sealed class AsyncHandler(Func<Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send();
    }
    private sealed class TrackedContent() : StringContent(Code) {
        internal readonly TaskCompletionSource<bool> Disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override void Dispose(bool disposing) { base.Dispose(disposing); Disposed.TrySetResult(true); }
    }
    private sealed class PendingStream(TaskCompletionSource<bool> started, TaskCompletionSource<int> released) : MemoryStream {
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            started.TrySetResult(true); return released.Task;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
            started.TrySetResult(true); return new(released.Task);
        }
    }
}
