namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private sealed class OpenAiCompatibleTestServer : IDisposable {
        private readonly TcpListener _listener;
        private readonly Task _loop;
        private readonly Func<string, string, string, (int Code, string Status, string Body, Dictionary<string, string>? Headers)> _handler;

        public OpenAiCompatibleTestServer(Func<string, string, string, (int Code, string Status, string Body, Dictionary<string, string>? Headers)> handler) {
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

            var contentLength = 0;
            foreach (var line in lines) {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) {
                    int.TryParse(line.Substring("Content-Length:".Length).Trim(), out contentLength);
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

            var (code, status, responseBody, headers) = _handler(method, path, body);
            var responseBytes = Encoding.UTF8.GetBytes(responseBody ?? string.Empty);
            var responseHeaderBuilder = new System.Text.StringBuilder();
            responseHeaderBuilder.Append($"HTTP/1.1 {code} {status}\r\n");
            responseHeaderBuilder.Append("Content-Type: application/json\r\n");
            if (headers is not null) {
                foreach (var kvp in headers) {
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

    private static void TestTrimPatchPreservesCrlf() {
        var patch = string.Join("\r\n", new[] {
            "diff --git a/file.txt b/file.txt",
            "index 123..456 100644",
            "--- a/file.txt",
            "+++ b/file.txt",
            "@@ -1,2 +1,2 @@",
            "-line1",
            "+line1a"
        });
        var trimmed = CallTrimPatch(patch, patch.Length - 2);
        AssertEqual(true, trimmed.Contains("\r\n", StringComparison.Ordinal), "crlf preserved");
    }

    private static void TestTrimPatchKeepsLastHunk() {
        var patch = string.Join("\n", new[] {
            "diff --git a/file.txt b/file.txt",
            "index 123..456 100644",
            "--- a/file.txt",
            "+++ b/file.txt",
            "@@ -1,1 +1,1 @@",
            "-line1",
            "+line1a",
            "@@ -10,1 +10,1 @@",
            "-line10",
            "+line10a",
            "@@ -20,1 +20,1 @@",
            "-line20",
            "+line20a"
        });

        var header = string.Join("\n", new[] {
            "diff --git a/file.txt b/file.txt",
            "index 123..456 100644",
            "--- a/file.txt",
            "+++ b/file.txt"
        });
        var first = string.Join("\n", new[] {
            "@@ -1,1 +1,1 @@",
            "-line1",
            "+line1a"
        });
        var last = string.Join("\n", new[] {
            "@@ -20,1 +20,1 @@",
            "-line20",
            "+line20a"
        });
        var marker = "... (truncated) ...";
        var target = string.Join("\n", new[] { header, first, marker, last });
        var maxChars = target.Length + 1;

        var trimmed = CallTrimPatch(patch, maxChars);
        AssertEqual(true, trimmed.Contains("@@ -1,1 +1,1 @@", StringComparison.Ordinal), "first hunk kept");
        AssertEqual(true, trimmed.Contains("@@ -20,1 +20,1 @@", StringComparison.Ordinal), "last hunk kept");
        AssertEqual(false, trimmed.Contains("@@ -10,1 +10,1 @@", StringComparison.Ordinal), "middle hunk skipped");
        AssertEqual(true, trimmed.Contains(marker, StringComparison.Ordinal), "marker present");
    }

    private static void TestTrimPatchKeepsTailHunkTwoHunks() {
        var newline = "\n";
        var headerLines = new[] {
            "diff --git a/file.txt b/file.txt",
            "index 123..456 100644",
            "--- a/file.txt",
            "+++ b/file.txt"
        };
        var hunk1Lines = new[] {
            "@@ -1,2 +1,2 @@",
            "-line1",
            "+line1a"
        };
        var hunk2Lines = new[] {
            "@@ -10,2 +10,2 @@",
            "-line10",
            "+line10a"
        };
        var header = string.Join(newline, headerLines);
        var hunk1 = string.Join(newline, hunk1Lines);
        var hunk2 = string.Join(newline, hunk2Lines);
        var patch = string.Join(newline, headerLines)
                    + newline + hunk1
                    + newline + hunk2;
        var marker = "... (truncated) ...";
        var maxChars = header.Length
                       + newline.Length + marker.Length
                       + newline.Length + hunk2.Length;
        var trimmed = CallTrimPatch(patch, maxChars);
        AssertEqual(false, trimmed.Contains("@@ -1,2 +1,2 @@", StringComparison.Ordinal), "first hunk removed");
        AssertEqual(true, trimmed.Contains("@@ -10,2 +10,2 @@", StringComparison.Ordinal), "tail hunk kept");
        AssertEqual(true, trimmed.Contains(marker, StringComparison.Ordinal), "marker added");
    }

    private static void TestReviewIntentAppliesFocus() {
        var settings = new ReviewSettings();
        ReviewIntents.Apply("security", settings);
        AssertSequenceEqual(new[] { "security", "auth", "secrets" }, settings.Focus, "intent focus");
    }

    private static void TestReviewIntentRespectsFocus() {
        var settings = new ReviewSettings { Focus = new[] { "custom" } };
        ReviewIntents.Apply("performance", settings);
        AssertSequenceEqual(new[] { "custom" }, settings.Focus, "intent preserves focus");
    }

    private static void TestReviewProviderAliasParsing() {
        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("openai", out var openai), "provider openai alias");
        AssertEqual(ReviewProvider.OpenAI, openai, "provider openai value");

        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("OpenAI", out var openaiMixedCase), "provider openai mixed case alias");
        AssertEqual(ReviewProvider.OpenAI, openaiMixedCase, "provider openai mixed case value");

        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("codex", out var codex), "provider codex alias");
        AssertEqual(ReviewProvider.OpenAI, codex, "provider codex value");

        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("CODEX", out var codexUpper), "provider codex uppercase alias");
        AssertEqual(ReviewProvider.OpenAI, codexUpper, "provider codex uppercase value");

        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("chatgpt", out var chatGpt), "provider chatgpt alias");
        AssertEqual(ReviewProvider.OpenAI, chatGpt, "provider chatgpt value");

        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("openai-codex", out var openAiCodex),
            "provider openai-codex alias");
        AssertEqual(ReviewProvider.OpenAI, openAiCodex, "provider openai-codex value");

        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("copilot", out var copilot), "provider copilot alias");
        AssertEqual(ReviewProvider.Copilot, copilot, "provider copilot value");

        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("Copilot", out var copilotMixedCase), "provider copilot mixed case alias");
        AssertEqual(ReviewProvider.Copilot, copilotMixedCase, "provider copilot mixed case value");

        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("openai-compatible", out var openaiCompat),
            "provider openai-compatible alias");
        AssertEqual(ReviewProvider.OpenAICompatible, openaiCompat, "provider openai-compatible value");

        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("ollama", out var ollama), "provider ollama alias");
        AssertEqual(ReviewProvider.OpenAICompatible, ollama, "provider ollama value");

        AssertEqual(false, ReviewProviderContracts.TryParseProviderAlias(null, out _), "provider null alias unsupported");
        AssertEqual(false, ReviewProviderContracts.TryParseProviderAlias("", out _), "provider empty alias unsupported");
        AssertEqual(false, ReviewProviderContracts.TryParseProviderAlias("   ", out _), "provider whitespace alias unsupported");
        AssertEqual(false, ReviewProviderContracts.TryParseProviderAlias("azure", out _), "provider azure alias unsupported");
    }

    private static void TestReviewProviderContractCapabilities() {
        var openai = ReviewProviderContracts.Get(ReviewProvider.OpenAI);
        AssertEqual(true, openai.SupportsUsageApi, "openai usage api");
        AssertEqual(true, openai.SupportsReasoningControls, "openai reasoning");
        AssertEqual(true, openai.RequiresOpenAiAuthStore, "openai auth");
        AssertEqual(true, openai.SupportsStreaming, "openai streaming");
        AssertEqual(true, openai.MaxRecommendedRetryCount > 0, "openai retry limit");

        var copilot = ReviewProviderContracts.Get(ReviewProvider.Copilot);
        AssertEqual(false, copilot.SupportsUsageApi, "copilot usage api");
        AssertEqual(false, copilot.SupportsReasoningControls, "copilot reasoning");
        AssertEqual(false, copilot.RequiresOpenAiAuthStore, "copilot auth");
        AssertEqual(true, copilot.SupportsStreaming, "copilot streaming");
        AssertEqual(true, copilot.MaxRecommendedRetryCount > 0, "copilot retry limit");

        var compatible = ReviewProviderContracts.Get(ReviewProvider.OpenAICompatible);
        AssertEqual(false, compatible.SupportsUsageApi, "openai-compatible usage api");
        AssertEqual(false, compatible.SupportsReasoningControls, "openai-compatible reasoning");
        AssertEqual(false, compatible.RequiresOpenAiAuthStore, "openai-compatible auth");
        AssertEqual(false, compatible.SupportsStreaming, "openai-compatible streaming");
        AssertEqual(true, compatible.MaxRecommendedRetryCount > 0, "openai-compatible retry limit");

        AssertThrows<NotSupportedException>(() => ReviewProviderContracts.Get((ReviewProvider)999), "unknown provider contract");
    }

    private static void TestReviewProviderConfigAlias() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, "{ \"review\": { \"provider\": \"codex\" } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings();
            ReviewConfigLoader.Apply(settings);
            AssertEqual(ReviewProvider.OpenAI, settings.Provider, "provider codex config");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestReviewProviderConfigInvalidThrows() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-invalid-provider-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, "{ \"review\": { \"provider\": \"open-ai\" } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            AssertThrows<InvalidOperationException>(() => {
                var settings = new ReviewSettings();
                ReviewConfigLoader.Apply(settings);
            }, "provider invalid config throws");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestReviewOpenAiCompatibleRejectsHttpNonLoopbackByDefault() {
        var settings = new ReviewSettings {
            Provider = ReviewProvider.OpenAICompatible,
            Model = "test-model",
            OpenAICompatibleBaseUrl = "http://example.com",
            OpenAICompatibleApiKey = "test",
            RetryCount = 1,
            RetryDelaySeconds = 1,
            RetryMaxDelaySeconds = 1,
            ProviderHealthChecks = false,
            Preflight = false,
            FailOpen = false,
            Diagnostics = false
        };

        AssertThrows<InvalidOperationException>(() => {
            var runner = new ReviewRunner(settings);
            runner.RunAsync("hi", onPartial: null, updateInterval: null, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }, "openai-compatible rejects http non-loopback by default");
    }

    private static void TestReviewOpenAiCompatiblePreflightTreats405AsReachable() {
        using var server = new OpenAiCompatibleTestServer((method, path, _) => {
            if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/models", StringComparison.OrdinalIgnoreCase)) {
                return (405, "Method Not Allowed", "{\"error\":\"nope\"}", null);
            }
            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/chat/completions", StringComparison.OrdinalIgnoreCase)) {
                return (200, "OK", "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}", null);
            }
            return (404, "Not Found", "{}", null);
        });

        var settings = new ReviewSettings {
            Provider = ReviewProvider.OpenAICompatible,
            ProviderHealthChecks = true,
            Preflight = false,
            Model = "test-model",
            OpenAICompatibleBaseUrl = server.BaseUri.ToString(),
            OpenAICompatibleApiKey = "test",
            OpenAICompatibleTimeoutSeconds = 10,
            RetryCount = 1,
            RetryDelaySeconds = 1,
            RetryMaxDelaySeconds = 1,
            FailOpen = false,
            Diagnostics = false
        };

        var runner = new ReviewRunner(settings);
        var result = runner.RunAsync("hi", onPartial: null, updateInterval: null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        AssertEqual("ok", result, "openai-compatible preflight treats 405 as reachable");
    }

    private static void TestReviewOpenAiCompatibleFollowsRedirects() {
        using var server = new OpenAiCompatibleTestServer((method, path, _) => {
            if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/models", StringComparison.OrdinalIgnoreCase)) {
                return (302, "Found", "{}", new Dictionary<string, string> {
                    ["Location"] = "/v1/models-redirected"
                });
            }
            if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/models-redirected", StringComparison.OrdinalIgnoreCase)) {
                return (405, "Method Not Allowed", "{\"error\":\"nope\"}", null);
            }

            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/chat/completions", StringComparison.OrdinalIgnoreCase)) {
                return (302, "Found", "{}", new Dictionary<string, string> {
                    ["Location"] = "/v1/chat/completions-redirected"
                });
            }
            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/chat/completions-redirected", StringComparison.OrdinalIgnoreCase)) {
                return (200, "OK", "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}", null);
            }

            return (404, "Not Found", "{}", null);
        });

        var settings = new ReviewSettings {
            Provider = ReviewProvider.OpenAICompatible,
            ProviderHealthChecks = true,
            Preflight = false,
            Model = "test-model",
            OpenAICompatibleBaseUrl = server.BaseUri.ToString(),
            OpenAICompatibleApiKey = "test",
            OpenAICompatibleTimeoutSeconds = 10,
            RetryCount = 1,
            RetryDelaySeconds = 1,
            RetryMaxDelaySeconds = 1,
            FailOpen = false,
            Diagnostics = false
        };

        var runner = new ReviewRunner(settings);
        var result = runner.RunAsync("hi", onPartial: null, updateInterval: null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        AssertEqual("ok", result, "openai-compatible follows redirects for preflight and request");
    }

    private static void TestReviewConfigLoaderReadsOpenAiAccountRotationCamelCase() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-rotation-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, "{ \"review\": { \"openaiAccountRotation\": \"round-robin\" } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings();
            ReviewConfigLoader.Apply(settings);
            AssertEqual("round-robin", settings.OpenAiAccountRotation, "review config loader openaiAccountRotation camelCase");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestReviewConfigLoaderReadsLegacyIncludeRelatedPullRequestsAlias() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-related-prs-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, "{ \"review\": { \"includeRelatedPullRequests\": false } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings {
                IncludeRelatedPrs = true
            };
            ReviewConfigLoader.Apply(settings);
            AssertEqual(false, settings.IncludeRelatedPrs, "review config loader includeRelatedPullRequests alias");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestReviewConfigLoaderPrefersCanonicalIncludeRelatedPrsWhenBothKeysPresent() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-related-prs-both-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, "{ \"review\": { \"includeRelatedPrs\": true, \"includeRelatedPullRequests\": false } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings {
                IncludeRelatedPrs = false
            };
            ReviewConfigLoader.Apply(settings);
            AssertEqual(true, settings.IncludeRelatedPrs, "review config loader includeRelatedPrs precedence over legacy alias");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestReviewProviderFallbackEnv() {
        var previousProvider = Environment.GetEnvironmentVariable("REVIEW_PROVIDER");
        var previousFallback = Environment.GetEnvironmentVariable("REVIEW_PROVIDER_FALLBACK");
        try {
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER", "openai");
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_FALLBACK", "copilot");
            var settings = ReviewSettings.FromEnvironment();
            AssertEqual(ReviewProvider.OpenAI, settings.Provider, "provider fallback env primary");
            AssertEqual(ReviewProvider.Copilot, settings.ProviderFallback, "provider fallback env value");

            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_FALLBACK", "azure");
            AssertThrows<InvalidOperationException>(() => ReviewSettings.FromEnvironment(), "provider fallback env invalid throws");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER", previousProvider);
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_FALLBACK", previousFallback);
        }
    }

    private static void TestReviewProviderEnvInvalidThrows() {
        var previousProvider = Environment.GetEnvironmentVariable("REVIEW_PROVIDER");
        try {
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER", "open-ai");
            AssertThrows<InvalidOperationException>(() => ReviewSettings.FromEnvironment(), "provider invalid env throws");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER", previousProvider);
        }
    }

    private static void TestReviewProviderFallbackConfig() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, "{ \"review\": { \"provider\": \"openai\", \"providerFallback\": \"copilot\" } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings();
            ReviewConfigLoader.Apply(settings);
            AssertEqual(ReviewProvider.OpenAI, settings.Provider, "provider fallback config primary");
            AssertEqual(ReviewProvider.Copilot, settings.ProviderFallback, "provider fallback config value");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestReviewProviderFallbackConfigInvalidThrows() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-fallback-invalid-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, "{ \"review\": { \"provider\": \"openai\", \"providerFallback\": \"azure\" } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            AssertThrows<InvalidOperationException>(() => {
                var settings = new ReviewSettings();
                ReviewConfigLoader.Apply(settings);
            }, "provider fallback config invalid throws");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestReviewProviderFallbackPlan() {
        AssertEqual(null, ReviewRunner.ResolveFallbackProvider(ReviewProvider.OpenAI, null), "provider fallback none");
        AssertEqual(null, ReviewRunner.ResolveFallbackProvider(ReviewProvider.OpenAI, ReviewProvider.OpenAI), "provider fallback same");
        AssertEqual(ReviewProvider.Copilot, ReviewRunner.ResolveFallbackProvider(ReviewProvider.OpenAI, ReviewProvider.Copilot),
            "provider fallback selected");
        AssertEqual(false, ReviewRunner.ShouldFallbackOnResult("Regular review content"), "provider fallback regular output");
        AssertEqual(true, ReviewRunner.ShouldFallbackOnResult($"x {ReviewDiagnostics.FailureMarker} y"), "provider fallback failure marker");
    }

    private static void TestReviewIntentAppliesDefaults() {
        var settings = new ReviewSettings();
        ReviewIntents.Apply("security", settings);
        AssertEqual("strict", settings.Strictness, "intent strictness");
        AssertContainsText(settings.Notes ?? string.Empty, "auth", "intent notes");
    }

    private static void TestReviewIntentRespectsSettings() {
        var settings = new ReviewSettings {
            Strictness = "custom",
            Notes = "custom notes"
        };
        ReviewIntents.Apply("maintainability", settings);
        AssertEqual("custom", settings.Strictness, "intent strictness preserved");
        AssertEqual("custom notes", settings.Notes, "intent notes preserved");
    }

    private static void TestReviewProviderHealthEnv() {
        var previousHealthChecks = Environment.GetEnvironmentVariable("REVIEW_PROVIDER_HEALTH_CHECKS");
        var previousHealthTimeout = Environment.GetEnvironmentVariable("REVIEW_PROVIDER_HEALTH_CHECK_TIMEOUT_SECONDS");
        var previousBreakerFailures = Environment.GetEnvironmentVariable("REVIEW_PROVIDER_CIRCUIT_BREAKER_FAILURES");
        var previousBreakerOpen = Environment.GetEnvironmentVariable("REVIEW_PROVIDER_CIRCUIT_BREAKER_OPEN_SECONDS");
        try {
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_HEALTH_CHECKS", "false");
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_HEALTH_CHECK_TIMEOUT_SECONDS", "7");
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_CIRCUIT_BREAKER_FAILURES", "5");
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_CIRCUIT_BREAKER_OPEN_SECONDS", "90");
            var settings = ReviewSettings.FromEnvironment();
            AssertEqual(false, settings.ProviderHealthChecks, "provider health checks env");
            AssertEqual(7, settings.ProviderHealthCheckTimeoutSeconds, "provider health timeout env");
            AssertEqual(5, settings.ProviderCircuitBreakerFailures, "provider breaker failures env");
            AssertEqual(90, settings.ProviderCircuitBreakerOpenSeconds, "provider breaker open env");

            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_CIRCUIT_BREAKER_FAILURES", "-1");
            settings = ReviewSettings.FromEnvironment();
            AssertEqual(3, settings.ProviderCircuitBreakerFailures, "provider breaker failures env invalid");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_HEALTH_CHECKS", previousHealthChecks);
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_HEALTH_CHECK_TIMEOUT_SECONDS", previousHealthTimeout);
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_CIRCUIT_BREAKER_FAILURES", previousBreakerFailures);
            Environment.SetEnvironmentVariable("REVIEW_PROVIDER_CIRCUIT_BREAKER_OPEN_SECONDS", previousBreakerOpen);
        }
    }

    private static void TestReviewProviderHealthConfig() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path,
                "{ \"review\": { \"providerHealthChecks\": false, \"providerHealthCheckTimeoutSeconds\": 9, " +
                "\"providerCircuitBreakerFailures\": 4, \"providerCircuitBreakerOpenSeconds\": 75 } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings();
            ReviewConfigLoader.Apply(settings);
            AssertEqual(false, settings.ProviderHealthChecks, "provider health checks config");
            AssertEqual(9, settings.ProviderHealthCheckTimeoutSeconds, "provider health timeout config");
            AssertEqual(4, settings.ProviderCircuitBreakerFailures, "provider breaker failures config");
            AssertEqual(75, settings.ProviderCircuitBreakerOpenSeconds, "provider breaker open config");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestReviewProviderCircuitBreaker() {
        ReviewProviderCircuitBreaker.Reset();
        var provider = ReviewProvider.OpenAI;
        var now = DateTimeOffset.UtcNow;

        AssertEqual(false, ReviewProviderCircuitBreaker.IsOpen(provider, now, out _), "provider breaker initially closed");

        ReviewProviderCircuitBreaker.RecordFailure(provider, 2, TimeSpan.FromSeconds(30), now);
        AssertEqual(false, ReviewProviderCircuitBreaker.IsOpen(provider, now, out _), "provider breaker first failure");

        ReviewProviderCircuitBreaker.RecordFailure(provider, 2, TimeSpan.FromSeconds(30), now);
        AssertEqual(true, ReviewProviderCircuitBreaker.IsOpen(provider, now, out var remaining), "provider breaker opens");
        AssertEqual(true, remaining.TotalSeconds > 0, "provider breaker remaining");

        AssertEqual(false, ReviewProviderCircuitBreaker.IsOpen(provider, now.AddSeconds(31), out _), "provider breaker closes");

        ReviewProviderCircuitBreaker.RecordFailure(provider, 1, TimeSpan.FromSeconds(30), now);
        AssertEqual(true, ReviewProviderCircuitBreaker.IsOpen(provider, now, out _), "provider breaker immediate open");
        ReviewProviderCircuitBreaker.RecordSuccess(provider);
        AssertEqual(false, ReviewProviderCircuitBreaker.IsOpen(provider, now, out _), "provider breaker reset by success");
        ReviewProviderCircuitBreaker.Reset();
    }

    private static void TestReviewIntentPerfAlias() {
        var settings = new ReviewSettings();
        ReviewIntents.Apply("perf", settings);
        AssertEqual("balanced", settings.Strictness, "perf alias strictness");
        AssertContainsText(settings.Notes ?? string.Empty, "allocations", "perf alias notes");
    }

    private static void TestReviewIntentNullSettings() {
        AssertThrows<ArgumentNullException>(() => ReviewIntents.Apply("security", null!), "intent null settings");
    }
}
#endif
