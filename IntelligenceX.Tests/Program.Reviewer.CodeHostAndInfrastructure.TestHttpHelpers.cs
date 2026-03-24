namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {

    private static string BuildGraphQlThreadsResponse() {
        return BuildGraphQlThreadsResponse("test");
    }

    private static string BuildGraphQlThreadsResponse(string body) {
        return BuildGraphQlThreadsResponse(body, "file.txt", 10, "bot", "thread1");
    }

    private static string BuildGraphQlThreadsResponse(string body, string path, int line, string author, string threadId,
        bool isResolved = false, bool isOutdated = false, int totalComments = 1) {
        return "{\"data\":{\"repository\":{\"pullRequest\":{\"reviewThreads\":{\"nodes\":[{\"id\":\""
            + EscapeJson(threadId)
            + "\",\"isResolved\":"
            + (isResolved ? "true" : "false")
            + ",\"isOutdated\":"
            + (isOutdated ? "true" : "false")
            + ",\"comments\":{\"totalCount\":"
            + totalComments.ToString()
            + ",\"nodes\":[{\"databaseId\":1,\"createdAt\":\"2024-01-01T00:00:00Z\",\"body\":\""
            + EscapeJson(body)
            + "\",\"path\":\""
            + EscapeJson(path)
            + "\",\"line\":"
            + line.ToString()
            + ",\"author\":{\"login\":\""
            + EscapeJson(author)
            + "\"}}]}}],\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null}}}}}}";
    }

    private static string EscapeJson(string value) {
        return value.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "")
            .Replace("\n", "\\n");
    }

    private static int GetQueryInt(string path, string key, int fallback) {
        var queryStart = path.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0 || queryStart == path.Length - 1) {
            return fallback;
        }
        var query = path.Substring(queryStart + 1);
        var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts) {
            var kvp = part.Split('=', 2);
            if (kvp.Length == 2 && string.Equals(kvp[0], key, StringComparison.OrdinalIgnoreCase)) {
                if (int.TryParse(kvp[1], out var value)) {
                    return value;
                }
            }
        }
        return fallback;
    }

    private static string BuildCompareFilesPage(int startIndex, int count) {
        var sb = new StringBuilder();
        sb.Append("{\"files\":[");
        for (var i = 0; i < count; i++) {
            if (i > 0) {
                sb.Append(",");
            }
            var name = $"file{startIndex + i}.txt";
            sb.Append("{\"filename\":\"").Append(name).Append("\",\"status\":\"modified\",\"patch\":\"@@\"}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static (int ExitCode, string Output) RunReviewerAndCaptureOutput(string[] args) {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try {
            var exitCode = ReviewerApp.RunAsync(args).GetAwaiter().GetResult();
            outWriter.Flush();
            errWriter.Flush();
            return (exitCode, outWriter.ToString() + errWriter.ToString());
        } finally {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    private sealed record HttpRequest(string Method, string Path, string Body, IReadOnlyDictionary<string, string> Headers);
    private sealed record HttpResponse(string Body, IReadOnlyDictionary<string, string>? Headers = null,
        int StatusCode = 200, string StatusText = "OK");

    private sealed class LocalHttpServer : IDisposable {
        private readonly TcpListener _listener;
        private readonly Func<HttpRequest, HttpResponse?> _handler;
        private readonly Task _loopTask;
        private bool _disposed;

        public LocalHttpServer(Func<HttpRequest, string?> handler)
            : this(request => {
                var body = handler(request);
                return body is null ? null : new HttpResponse(body);
            }) { }

        public LocalHttpServer(Func<HttpRequest, HttpResponse?> handler) {
            _handler = handler;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            BaseUri = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
            _loopTask = Task.Run(LoopAsync);
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
                    await HandleClientAsync(client).ConfigureAwait(false);
                }
            } catch {
                // Ignore listener failures in tests.
            }
        }

        private async Task HandleClientAsync(TcpClient client) {
            using var _ = client;
            using var stream = client.GetStream();
            var request = await ReadRequestAsync(stream).ConfigureAwait(false);
            if (request is null) {
                return;
            }

            var response = _handler(request);
            if (response is null) {
                await WriteResponseAsync(stream, 404, "Not Found", "{}").ConfigureAwait(false);
                return;
            }

            await WriteResponseAsync(stream, response.StatusCode, response.StatusText, response.Body, response.Headers)
                .ConfigureAwait(false);
        }

        private static async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream) {
            var headerBytes = new List<byte>(1024);
            var delimiter = new byte[] { 13, 10, 13, 10 };
            var matched = 0;
            var singleByte = new byte[1];

            while (true) {
                var read = await stream.ReadAsync(singleByte, 0, 1).ConfigureAwait(false);
                if (read == 0) {
                    return null;
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
            var headerLines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (headerLines.Length == 0 || string.IsNullOrWhiteSpace(headerLines[0])) {
                return null;
            }

            var requestParts = headerLines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (requestParts.Length < 2) {
                return null;
            }

            var contentLength = 0;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < headerLines.Length; i++) {
                var line = headerLines[i];
                if (string.IsNullOrEmpty(line)) {
                    break;
                }
                var headerParts = line.Split(':', 2);
                if (headerParts.Length == 2) {
                    var headerName = headerParts[0].Trim();
                    var headerValue = headerParts[1].Trim();
                    headers[headerName] = headerValue;
                    if (headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) {
                        int.TryParse(headerValue, out contentLength);
                    }
                }
            }

            var body = string.Empty;
            if (contentLength > 0) {
                var bodyBytes = new byte[contentLength];
                var bodyRead = 0;
                while (bodyRead < contentLength) {
                    var count = await stream.ReadAsync(bodyBytes, bodyRead, contentLength - bodyRead).ConfigureAwait(false);
                    if (count == 0) {
                        break;
                    }
                    bodyRead += count;
                }
                if (bodyRead > 0) {
                    body = Encoding.UTF8.GetString(bodyBytes, 0, bodyRead);
                }
            }

            return new HttpRequest(requestParts[0], requestParts[1], body, headers);
        }

        private static async Task WriteResponseAsync(NetworkStream stream, int statusCode, string statusText, string body,
            IReadOnlyDictionary<string, string>? headers = null) {
            var payload = Encoding.UTF8.GetBytes(body);
            var sb = new StringBuilder();
            sb.AppendLine($"HTTP/1.1 {statusCode} {statusText}");
            sb.AppendLine("Content-Type: application/json");
            sb.AppendLine($"Content-Length: {payload.Length}");
            if (headers is not null) {
                foreach (var header in headers) {
                    sb.AppendLine($"{header.Key}: {header.Value}");
                }
            }
            sb.AppendLine("Connection: close");
            sb.AppendLine();
            var headerBytes = Encoding.UTF8.GetBytes(sb.ToString());
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
            await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            _disposed = true;
            _listener.Stop();
            try {
                _loopTask.GetAwaiter().GetResult();
            } catch {
                // ignored
            }
        }
    }
}
#endif
