using System.Net;
using System.Text;
using IntelligenceX.OpenAI.CompatibleHttp;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class ModelCatalogResourceTests {
    [Fact]
    public async Task OversizedSupplementaryCatalogKeepsPrimaryModelsWithinTheReadLimit() {
        const int maximumBytes = 4_194_304;
        using var body = new CountingStream(Encoding.UTF8.GetBytes(new string(' ', maximumBytes + 100)
            + "{\"data\":[{\"id\":\"model\",\"state\":\"loaded\"}]}"));
        using var http = new HttpClient(new Handler(request => Task.FromResult(IsPrimary(request)
            ? PrimaryResponse()
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) })));
        using var transport = CreateTransport(http);

        var result = await transport.ListModelsAsync(default);

        Assert.Equal("model", Assert.Single(result.Models).Model);
        Assert.Null(result.Models[0].RuntimeState);
        Assert.InRange(body.BytesRead, 1, maximumBytes + 1);
        Assert.True(body.IsDisposed);
    }

    [Fact]
    public async Task CancellationBoundsANonCooperativeSupplementaryCatalogRequest() {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var body = new CountingStream(Array.Empty<byte>());
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) };
        using var http = new HttpClient(new Handler(request => {
            if (IsPrimary(request)) return Task.FromResult(PrimaryResponse());
            entered.TrySetResult(true);
            return lateResponse.Task;
        }));
        using var transport = CreateTransport(http);
        using var cancellation = new CancellationTokenSource();
        var completions = new List<IntelligenceX.Telemetry.RpcCallCompletedEventArgs>();
        transport.RpcCallCompleted += (_, args) => completions.Add(args);
        var pending = transport.ListModelsAsync(cancellation.Token);
        try {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            cancellation.Cancel();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.False(Assert.Single(completions).Success);
        } finally {
            lateResponse.TrySetResult(response);
            await Record.ExceptionAsync(() => pending);
        }
        await body.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private static bool IsPrimary(HttpRequestMessage request) => request.RequestUri!.AbsolutePath == "/v1/models";
    private static HttpResponseMessage PrimaryResponse() => new(HttpStatusCode.OK) {
        Content = new StringContent("{\"data\":[{\"id\":\"model\"}]}")
    };
    private static OpenAICompatibleHttpTransport CreateTransport(HttpClient http) => new(new() {
        BaseUrl = "http://127.0.0.1:1234/v1", AllowInsecureHttp = true, Streaming = false
    }, http);

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes) {
        internal int BytesRead { get; private set; }
        internal bool IsDisposed { get; private set; }
        internal TaskCompletionSource<bool> Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            var read = await base.ReadAsync(buffer, offset, count, cancellationToken);
            BytesRead += read;
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
            var read = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
            CopyCoreAsync(destination, bufferSize, cancellationToken);
        private async Task CopyCoreAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) {
            var buffer = new byte[bufferSize];
            int read;
            while ((read = await ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                await destination.WriteAsync(buffer, 0, read, cancellationToken);
        }
        protected override void Dispose(bool disposing) {
            IsDisposed = true;
            Disposed.TrySetResult(true);
            base.Dispose(disposing);
        }
    }
}
