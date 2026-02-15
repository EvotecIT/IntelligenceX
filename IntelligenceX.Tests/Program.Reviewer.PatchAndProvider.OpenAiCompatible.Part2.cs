namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestReviewOpenAiCompatibleDoesNotLeakErrorBodyWhenDiagnosticsFalse() {
        const string secret = "SECRET_TOKEN_123";
        using var server = new OpenAiCompatibleTestServer((method, path, _, _) => {
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
        using var server = new OpenAiCompatibleTestServer((method, path, _, _) => {
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
    private static void TestReviewOpenAiCompatiblePreservesPostBodyAcrossRedirects() {
        var requestBodies = new List<string>();
        var requestMethods = new List<string>();
        var requestAuth = new List<string>();
        using var server = new OpenAiCompatibleTestServer((method, path, body, headers) => {
            requestMethods.Add(method);
            requestBodies.Add(body);
            requestAuth.Add(headers.TryGetValue("Authorization", out var auth) ? auth : string.Empty);
            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/chat/completions", StringComparison.OrdinalIgnoreCase)) {
                return (302, "Found", "{}", new Dictionary<string, string> {
                    ["Location"] = "/v1/chat/completions2"
                });
            }
            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/chat/completions2", StringComparison.OrdinalIgnoreCase)) {
                return (302, "Found", "{}", new Dictionary<string, string> {
                    ["Location"] = "/v1/chat/completions3"
                });
            }
            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/chat/completions3", StringComparison.OrdinalIgnoreCase)) {
                return (200, "OK", "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}", null);
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

        var runner = new ReviewRunner(settings);
        var output = runner.RunAsync("hi", onPartial: null, updateInterval: null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        AssertContainsText(output, "ok", "openai-compatible output" );

        AssertEqual(3, requestBodies.Count, "openai-compatible request count (redirect chain)");
        AssertEqual(true, requestBodies[0].Length > 0, "openai-compatible first request has body");
        AssertEqual(requestBodies[0], requestBodies[1], "openai-compatible redirect preserves body (2)");
        AssertEqual(requestBodies[0], requestBodies[2], "openai-compatible redirect preserves body (3)");
        AssertEqual("POST", requestMethods[0].ToUpperInvariant(), "openai-compatible method (1)");
        AssertEqual("POST", requestMethods[1].ToUpperInvariant(), "openai-compatible method (2)");
        AssertEqual("POST", requestMethods[2].ToUpperInvariant(), "openai-compatible method (3)");
        AssertContainsText(requestAuth[0], "Bearer test", "openai-compatible sends Authorization on first request");
        AssertContainsText(requestAuth[1], "Bearer test", "openai-compatible sends Authorization on redirect (2)");
        AssertContainsText(requestAuth[2], "Bearer test", "openai-compatible sends Authorization on redirect (3)");
    }

    private static void TestReviewOpenAiCompatibleDropsAuthorizationOnRedirectWhenEnabled() {
        var requestAuth = new List<string>();
        using var server = new OpenAiCompatibleTestServer((method, path, body, headers) => {
            requestAuth.Add(headers.TryGetValue("Authorization", out var auth) ? auth : string.Empty);
            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/chat/completions", StringComparison.OrdinalIgnoreCase)) {
                return (302, "Found", "{}", new Dictionary<string, string> {
                    ["Location"] = "/v1/chat/completions2"
                });
            }
            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/chat/completions2", StringComparison.OrdinalIgnoreCase)) {
                return (302, "Found", "{}", new Dictionary<string, string> {
                    ["Location"] = "/v1/chat/completions3"
                });
            }
            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/chat/completions3", StringComparison.OrdinalIgnoreCase)) {
                return (200, "OK", "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}", null);
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
            OpenAICompatibleDropAuthorizationOnRedirect = true,
            RetryCount = 1,
            RetryDelaySeconds = 1,
            RetryMaxDelaySeconds = 1,
            FailOpen = false,
            Diagnostics = false
        };

        var runner = new ReviewRunner(settings);
        var output = runner.RunAsync("hi", onPartial: null, updateInterval: null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        AssertContainsText(output, "ok", "openai-compatible output (drop auth on redirect)");

        AssertEqual(3, requestAuth.Count, "openai-compatible request count (drop auth redirect chain)");
        AssertContainsText(requestAuth[0], "Bearer test", "openai-compatible sends Authorization on first request (drop auth)");
        AssertEqual(string.Empty, requestAuth[1], "openai-compatible drops Authorization on redirect (2) when enabled");
        AssertEqual(string.Empty, requestAuth[2], "openai-compatible drops Authorization on redirect (3) when enabled");
    }

}
#endif
