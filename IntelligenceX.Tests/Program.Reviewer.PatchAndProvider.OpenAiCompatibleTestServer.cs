namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private sealed class OpenAiCompatibleTestServer : IDisposable {
        private readonly TcpListener _listener;
        private readonly Task _loop;
        private readonly Func<string, string, string, Dictionary<string, string>, (int Code, string Status, string Body, Dictionary<string, string>? Headers)> _handler;

        public OpenAiCompatibleTestServer(Func<string, string, string, Dictionary<string, string>, (int Code, string Status, string Body, Dictionary<string, string>? Headers)> handler) {
            _handler = handler;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            BaseUri = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
            _loop = Task.Run(LoopAsync);
        }

        public Uri BaseUri { get; }

        private async Task LoopAsync() {
            try {
                while (true) {
                    TcpClient client;
                    try {
                        client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    } catch (ObjectDisposedException) {
                        break;
                    }

                    _ = Task.Run(() => HandleClientAsync(client));
                }
            } catch {
                // Ignore test server loop failures.
            }
        }

        private async Task HandleClientAsync(TcpClient client) {
            using var _ = client;
            using var stream = client.GetStream();

            var headerBytes = new List<byte>(1024);
            var delimiter = new byte[] { 13, 10, 13, 10 };
            var matched = 0;
            var singleByte = new byte[1];

            while (true) {
                var read = await stream.ReadAsync(singleByte, 0, 1).ConfigureAwait(false);
                if (read == 0) {
                    return;
                }
                var b = singleByte[0];
                headerBytes.Add(b);

                if (b == delimiter[matched]) {
                    matched++;
                    if (matched == delimiter.Length) {
                        break;
                    }
                } else {
                    matched = b == delimiter[0] ? 1 : 0;
                }
            }

            var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var requestLine = lines.Length > 0 ? lines[0] : string.Empty;
            var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) {
                return;
            }
            var method = parts[0];
            var path = parts[1];

            var requestHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var contentLength = 0;
            for (var i = 1; i < lines.Length; i++) {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) {
                    break;
                }
                var colon = line.IndexOf(':');
                if (colon <= 0) {
                    continue;
                }
                var key = line.Substring(0, colon).Trim();
                var value = line.Substring(colon + 1).Trim();
                requestHeaders[key] = value;
                if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) {
                    int.TryParse(value, out contentLength);
                }
            }

            var body = string.Empty;
            if (contentLength > 0) {
                var buffer = new byte[contentLength];
                var total = 0;
                while (total < contentLength) {
                    var read = await stream.ReadAsync(buffer, total, contentLength - total).ConfigureAwait(false);
                    if (read == 0) {
                        break;
                    }
                    total += read;
                }
                body = Encoding.UTF8.GetString(buffer, 0, total);
            }

            var (code, status, responseBody, responseHeaders) = _handler(method, path, body, requestHeaders);
            var responseBytes = Encoding.UTF8.GetBytes(responseBody ?? string.Empty);
            var responseHeaderBuilder = new System.Text.StringBuilder();
            responseHeaderBuilder.Append($"HTTP/1.1 {code} {status}\r\n");
            responseHeaderBuilder.Append("Content-Type: application/json\r\n");
            if (responseHeaders is not null) {
                foreach (var kvp in responseHeaders) {
                    responseHeaderBuilder.Append(kvp.Key);
                    responseHeaderBuilder.Append(": ");
                    responseHeaderBuilder.Append(kvp.Value);
                    responseHeaderBuilder.Append("\r\n");
                }
            }
            responseHeaderBuilder.Append($"Content-Length: {responseBytes.Length}\r\n");
            responseHeaderBuilder.Append("Connection: close\r\n");
            responseHeaderBuilder.Append("\r\n");

            var responseHeader =
                responseHeaderBuilder.ToString();
            var headerOut = Encoding.ASCII.GetBytes(responseHeader);
            await stream.WriteAsync(headerOut, 0, headerOut.Length).ConfigureAwait(false);
            await stream.WriteAsync(responseBytes, 0, responseBytes.Length).ConfigureAwait(false);
        }

        public void Dispose() {
            _listener.Stop();
            try {
                _loop.Wait(TimeSpan.FromSeconds(1));
            } catch {
                // Ignore.
            }
        }
    }
}
#endif
