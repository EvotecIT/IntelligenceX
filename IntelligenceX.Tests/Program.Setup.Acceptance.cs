using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestSetupWizardPlainReachesPullRequestCreatedWithFakeGitHubApi() {
        using var server = new SetupFakeGitHubApiServer("owner", "repo");
        var authB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"kind\":\"test\"}"));
        var args = new[] {
            "--plain",
            "--repo", "owner/repo",
            "--github-token", "test-token",
            "--github-api-base-url", server.BaseUri.ToString().TrimEnd('/'),
            "--with-config",
            "--manual-secret",
            "--auth-b64", authB64,
            "--branch", "intelligencex-setup/test-wizard-acceptance"
        };

        var (exitCode, output) = RunWizardAndCaptureOutput(args);
        AssertEqual(0, exitCode, "setup wizard plain fake API exit");
        AssertContainsText(output, "Falling back to setup options", "setup wizard plain fallback message");
        AssertContainsText(output, "Setup complete. PR created:", "setup wizard plain pr created message");
        AssertContainsText(output, server.PullRequestUrl, "setup wizard plain pr url");
        AssertEqual(1, server.PullRequestCreateCount, "setup wizard plain pr create request count");
        AssertEqual(true, server.SawWorkflowWrite, "setup wizard plain workflow write");
        AssertEqual(true, server.SawReviewerConfigWrite, "setup wizard plain reviewer config write");
    }

    private static void TestWebSetupArgsCanReachPullRequestCreatedWithFakeGitHubApi() {
        using var server = new SetupFakeGitHubApiServer("owner", "repo");
        var authB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"kind\":\"test\"}"));
        var args = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForAcceptanceTests(
            repo: "owner/repo",
            gitHubToken: "test-token",
            withConfig: true,
            skipSecret: false,
            manualSecret: true,
            authB64: authB64,
            branchName: "intelligencex-setup/test-web-acceptance");

        var previousApiBaseUrl = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_API_BASE_URL");
        try {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_GITHUB_API_BASE_URL", server.BaseUri.ToString().TrimEnd('/'));
            var (exitCode, output) = RunSetupAndCaptureOutput(args);
            AssertEqual(0, exitCode, "web setup args fake API exit");
            AssertContainsText(output, "Setup complete. PR created:", "web setup args pr created message");
            AssertContainsText(output, server.PullRequestUrl, "web setup args pr url");
            AssertEqual(1, server.PullRequestCreateCount, "web setup args pr create request count");
            AssertEqual(true, server.SawWorkflowWrite, "web setup args workflow write");
            AssertEqual(true, server.SawReviewerConfigWrite, "web setup args reviewer config write");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_GITHUB_API_BASE_URL", previousApiBaseUrl);
        }
    }

    private static (int ExitCode, string Output) RunWizardAndCaptureOutput(string[] args) {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try {
            var exitCode = IntelligenceX.Cli.Setup.Wizard.WizardRunner.RunAsync(args).GetAwaiter().GetResult();
            outWriter.Flush();
            errWriter.Flush();
            return (exitCode, outWriter.ToString() + errWriter.ToString());
        } finally {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    private static (int ExitCode, string Output) RunSetupAndCaptureOutput(string[] args) {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try {
            var exitCode = IntelligenceX.Cli.Setup.SetupRunner.RunAsync(args).GetAwaiter().GetResult();
            outWriter.Flush();
            errWriter.Flush();
            return (exitCode, outWriter.ToString() + errWriter.ToString());
        } finally {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    private sealed class SetupFakeGitHubApiServer : IDisposable {
        private static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);
        private readonly string _repoPrefix;
        private readonly string _pullRequestUrl;
        private readonly TcpListener _listener;
        private readonly Task _loopTask;
        private readonly object _sync = new();
        private readonly HashSet<string> _writtenPaths = new(StringComparer.Ordinal);
        private int _pullRequestCreateCount;
        private bool _disposed;

        public SetupFakeGitHubApiServer(string owner, string repo) {
            _repoPrefix = $"/repos/{owner}/{repo}";
            _pullRequestUrl = $"https://github.com/{owner}/{repo}/pull/999";
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            BaseUri = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
            _loopTask = Task.Run(LoopAsync);
        }

        public Uri BaseUri { get; }
        public string PullRequestUrl => _pullRequestUrl;
        public int PullRequestCreateCount {
            get {
                lock (_sync) {
                    return _pullRequestCreateCount;
                }
            }
        }

        public bool SawWorkflowWrite {
            get {
                lock (_sync) {
                    return _writtenPaths.Contains($"{_repoPrefix}/contents/.github/workflows/review-intelligencex.yml");
                }
            }
        }

        public bool SawReviewerConfigWrite {
            get {
                lock (_sync) {
                    return _writtenPaths.Contains($"{_repoPrefix}/contents/.intelligencex/reviewer.json");
                }
            }
        }

        private async Task LoopAsync() {
            while (true) {
                TcpClient client;
                try {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                } catch (ObjectDisposedException) {
                    break;
                } catch (SocketException) when (_disposed) {
                    break;
                }
                await HandleClientAsync(client).ConfigureAwait(false);
            }
        }

        private async Task HandleClientAsync(TcpClient client) {
            using (client) {
                using var stream = client.GetStream();
                using var readCts = new CancellationTokenSource(RequestReadTimeout);
                SetupFakeHttpRequest? request;
                try {
                    request = await ReadRequestAsync(stream, readCts.Token).ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    return;
                }
                if (request is null) {
                    return;
                }

                var response = BuildResponse(request);
                await WriteResponseAsync(stream, response.StatusCode, response.StatusText, response.Body).ConfigureAwait(false);
            }
        }

        private SetupFakeHttpResponse BuildResponse(SetupFakeHttpRequest request) {
            var method = request.Method;
            var path = request.Path;
            var pathWithoutQuery = path;
            var queryIndex = pathWithoutQuery.IndexOf('?', StringComparison.Ordinal);
            if (queryIndex >= 0) {
                pathWithoutQuery = pathWithoutQuery[..queryIndex];
            }

            if (string.Equals(method, "GET", StringComparison.Ordinal) &&
                pathWithoutQuery.StartsWith("/user/repos", StringComparison.Ordinal)) {
                return new SetupFakeHttpResponse(
                    200,
                    "OK",
                    "[{\"full_name\":\"owner/repo\",\"private\":true,\"updated_at\":\"2026-02-12T00:00:00Z\"}]");
            }

            if (string.Equals(method, "GET", StringComparison.Ordinal) &&
                string.Equals(pathWithoutQuery, $"{_repoPrefix}", StringComparison.Ordinal)) {
                return new SetupFakeHttpResponse(200, "OK", "{\"default_branch\":\"main\"}");
            }

            if (string.Equals(method, "GET", StringComparison.Ordinal) &&
                string.Equals(pathWithoutQuery, $"{_repoPrefix}/git/ref/heads/main", StringComparison.Ordinal)) {
                return new SetupFakeHttpResponse(200, "OK",
                    "{\"object\":{\"sha\":\"1234567890abcdef1234567890abcdef12345678\"}}");
            }

            if (string.Equals(method, "POST", StringComparison.Ordinal) &&
                string.Equals(pathWithoutQuery, $"{_repoPrefix}/git/refs", StringComparison.Ordinal)) {
                return new SetupFakeHttpResponse(201, "Created", "{}");
            }

            if (string.Equals(method, "GET", StringComparison.Ordinal) &&
                pathWithoutQuery.StartsWith($"{_repoPrefix}/contents/", StringComparison.Ordinal)) {
                return new SetupFakeHttpResponse(404, "Not Found", "{\"message\":\"Not Found\"}");
            }

            if (string.Equals(method, "PUT", StringComparison.Ordinal) &&
                pathWithoutQuery.StartsWith($"{_repoPrefix}/contents/", StringComparison.Ordinal)) {
                lock (_sync) {
                    _writtenPaths.Add(pathWithoutQuery);
                }
                return new SetupFakeHttpResponse(200, "OK", "{}");
            }

            if (string.Equals(method, "POST", StringComparison.Ordinal) &&
                string.Equals(pathWithoutQuery, $"{_repoPrefix}/pulls", StringComparison.Ordinal)) {
                lock (_sync) {
                    _pullRequestCreateCount++;
                }
                return new SetupFakeHttpResponse(201, "Created",
                    $"{{\"html_url\":\"{_pullRequestUrl}\"}}");
            }

            return new SetupFakeHttpResponse(404, "Not Found", "{\"message\":\"Not Found\"}");
        }

        private static async Task<SetupFakeHttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken) {
            var headerBytes = new List<byte>(1024);
            var delimiter = new byte[] { 13, 10, 13, 10 };
            var matched = 0;
            var singleByte = new byte[1];

            while (true) {
                var read = await stream.ReadAsync(singleByte, 0, 1, cancellationToken).ConfigureAwait(false);
                if (read == 0) {
                    return null;
                }

                var value = singleByte[0];
                headerBytes.Add(value);
                if (value == delimiter[matched]) {
                    matched++;
                    if (matched == delimiter.Length) {
                        break;
                    }
                } else {
                    matched = value == delimiter[0] ? 1 : 0;
                }
            }

            var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0])) {
                return null;
            }

            var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (requestLine.Length < 2) {
                return null;
            }

            var contentLength = 0;
            for (var i = 1; i < lines.Length; i++) {
                var line = lines[i];
                if (string.IsNullOrEmpty(line)) {
                    break;
                }
                var headerParts = line.Split(':', 2);
                if (headerParts.Length == 2 &&
                    string.Equals(headerParts[0].Trim(), "Content-Length", StringComparison.OrdinalIgnoreCase)) {
                    int.TryParse(headerParts[1].Trim(), out contentLength);
                }
            }

            var body = string.Empty;
            if (contentLength > 0) {
                var bodyBytes = new byte[contentLength];
                var read = 0;
                while (read < contentLength) {
                    var count = await stream.ReadAsync(bodyBytes, read, contentLength - read, cancellationToken)
                        .ConfigureAwait(false);
                    if (count == 0) {
                        break;
                    }
                    read += count;
                }
                if (read > 0) {
                    body = Encoding.UTF8.GetString(bodyBytes, 0, read);
                }
            }

            return new SetupFakeHttpRequest(requestLine[0], requestLine[1], body);
        }

        private static async Task WriteResponseAsync(NetworkStream stream, int statusCode, string statusText, string body) {
            var payload = Encoding.UTF8.GetBytes(body);
            var builder = new StringBuilder();
            builder.AppendLine($"HTTP/1.1 {statusCode} {statusText}");
            builder.AppendLine("Content-Type: application/json");
            builder.AppendLine($"Content-Length: {payload.Length}");
            builder.AppendLine("Connection: close");
            builder.AppendLine();

            var headerBytes = Encoding.UTF8.GetBytes(builder.ToString());
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
            var completed = Task.WhenAny(_loopTask, Task.Delay(ShutdownTimeout)).GetAwaiter().GetResult();
            if (!ReferenceEquals(completed, _loopTask)) {
                throw new TimeoutException("Setup fake GitHub API server did not stop in time.");
            }
            _loopTask.GetAwaiter().GetResult();
        }
    }

    private sealed record SetupFakeHttpRequest(string Method, string Path, string Body);
    private sealed record SetupFakeHttpResponse(int StatusCode, string StatusText, string Body);
#endif
}
