using System.Net;
using System.Net.Sockets;
using System.Text;
using IntelligenceX.Ledger.Contracts;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerReadClientTests {
    [Fact]
    public async Task RequestDeadlineCoversAResponseThatStallsAfterSuccessfulHeaders() {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var serverStop = new CancellationTokenSource();
        using var callerDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var receive = Task.Run(async () => {
            using var socket = await listener.AcceptTcpClientAsync(serverStop.Token);
            await using var stream = socket.GetStream();
            await HeadersAsync(stream, serverStop.Token);
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\n"u8.ToArray(), serverStop.Token);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, serverStop.Token); }
            catch (OperationCanceledException) when (serverStop.IsCancellationRequested) { }
        });
        using var client = new LedgerReadClient(new() { Endpoint = $"http://127.0.0.1:{port}/", ReadToken = new('r', 32) });
        try {
            await Assert.ThrowsAsync<TaskCanceledException>(() => client.WorkAsync(callerDeadline.Token));
            Assert.False(callerDeadline.IsCancellationRequested);
        } finally { serverStop.Cancel(); await receive; }
    }

    [Fact]
    public async Task RedirectIsRejectedAtTheAuthenticatedOrigin() {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var receive = Task.Run(async () => {
            using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
            await using var stream = socket.GetStream();
            var headers = await HeadersAsync(stream, deadline.Token);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:1/credential-target\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), deadline.Token);
            return headers;
        });
        using var client = new LedgerReadClient(new() { Endpoint = $"http://127.0.0.1:{port}/", ReadToken = new('r', 32) });

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.WorkAsync(deadline.Token));

        Assert.Equal(HttpStatusCode.Found, error.StatusCode);
        var request = await receive;
        Assert.StartsWith("GET /api/ledger/v1/work?limit=200 HTTP/1.1\r\n", request);
        Assert.Contains("Authorization: Bearer " + new string('r', 32) + "\r\n", request);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedResponseIsRejectedWithAndWithoutContentLength(bool chunked) {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var receive = Task.Run(async () => {
            using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
            await using var stream = socket.GetStream();
            await HeadersAsync(stream, deadline.Token);
            const int size = 2 * 1024 * 1024 + 1;
            var framing = chunked ? "Transfer-Encoding: chunked\r\n" : "Content-Length: " + size + "\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\n" + framing + "Connection: close\r\n\r\n"), deadline.Token);
            if (!chunked) return; // The declared length alone must reject the response.
            await stream.WriteAsync(Encoding.ASCII.GetBytes(size.ToString("X") + "\r\n"), deadline.Token);
            await stream.WriteAsync(new byte[size], deadline.Token);
            await stream.WriteAsync("\r\n0\r\n\r\n"u8.ToArray(), deadline.Token);
        });
        using var client = new LedgerReadClient(new() { Endpoint = $"http://127.0.0.1:{port}/", ReadToken = new('r', 32) });

        await Assert.ThrowsAsync<InvalidDataException>(() => client.WorkAsync(deadline.Token));
        await receive;
    }

    private static async Task<string> HeadersAsync(NetworkStream stream, CancellationToken token) {
        var headers = new StringBuilder();
        var next = new byte[1];
        while (headers.Length < 8192 && await stream.ReadAsync(next, token) == 1) {
            headers.Append((char)next[0]);
            if (headers.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal)) return headers.ToString();
        }
        throw new InvalidDataException("Synthetic HTTP request headers were incomplete.");
    }
}
