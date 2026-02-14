namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestReviewOpenAiCompatibleDoesNotLeakErrorBodyWhenDiagnosticsFalse() {
        const string secret = "SECRET_TOKEN_123";
        using var server = new OpenAiCompatibleTestServer((method, path, _) => {
            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/chat/completions", StringComparison.OrdinalIgnoreCase)) {
                return (401, "Unauthorized", "{\"authorization\":\"Bearer " + secret + "\"}", null);
            }
            return (404, "Not Found", "{}", null);
        });

        var settings = new ReviewSettings {
            Provider = ReviewProvider.OpenAICompatible,
            ProviderHealthChecks = false,
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

        try {
            var runner = new ReviewRunner(settings);
            runner.RunAsync("hi", onPartial: null, updateInterval: null, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            AssertEqual(true, false, "openai-compatible should throw when fail-open is disabled");
        } catch (InvalidOperationException ex) {
            AssertEqual(false, ex.Message.Contains(secret, StringComparison.Ordinal), "openai-compatible non-diagnostics omits remote body");
        }
    }

    private static void TestReviewOpenAiCompatibleRejectsCrossHostRedirects() {
        using var server = new OpenAiCompatibleTestServer((method, path, _) => {
            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/chat/completions", StringComparison.OrdinalIgnoreCase)) {
                return (302, "Found", "{}", new Dictionary<string, string> {
                    ["Location"] = "http://evil.example/v1/chat/completions"
                });
            }
            return (404, "Not Found", "{}", null);
        });

        var settings = new ReviewSettings {
            Provider = ReviewProvider.OpenAICompatible,
            ProviderHealthChecks = false,
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

        try {
            var runner = new ReviewRunner(settings);
            runner.RunAsync("hi", onPartial: null, updateInterval: null, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            AssertEqual(true, false, "openai-compatible should throw on cross-host redirects");
        } catch (InvalidOperationException ex) {
            AssertContainsText(ex.Message, "different host", "openai-compatible rejects cross-host redirect");
        }
    }
}
#endif
