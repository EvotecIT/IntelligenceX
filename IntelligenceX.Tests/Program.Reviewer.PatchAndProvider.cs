namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
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

        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("openrouter", out var openrouter), "provider openrouter alias");
        AssertEqual(ReviewProvider.OpenAICompatible, openrouter, "provider openrouter value");

        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("claude", out var claude), "provider claude alias");
        AssertEqual(ReviewProvider.Claude, claude, "provider claude value");

        AssertEqual(true, ReviewProviderContracts.TryParseProviderAlias("anthropic", out var anthropic), "provider anthropic alias");
        AssertEqual(ReviewProvider.Claude, anthropic, "provider anthropic value");

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

        var claude = ReviewProviderContracts.Get(ReviewProvider.Claude);
        AssertEqual(true, claude.SupportsUsageApi, "claude usage api");
        AssertEqual(false, claude.SupportsReasoningControls, "claude reasoning");
        AssertEqual(false, claude.RequiresOpenAiAuthStore, "claude auth");
        AssertEqual(false, claude.SupportsStreaming, "claude streaming");
        AssertEqual(true, claude.MaxRecommendedRetryCount > 0, "claude retry limit");

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

    private static void TestReviewClaudeProviderConfigAlias() {
        var previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var path = Path.Combine(Path.GetTempPath(), $"intelligencex-review-claude-{Guid.NewGuid():N}.json");
        try {
            File.WriteAllText(path, "{ \"review\": { \"provider\": \"anthropic\" } }");
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            var settings = new ReviewSettings();
            ReviewConfigLoader.Apply(settings);
            AssertEqual(ReviewProvider.Claude, settings.Provider, "provider anthropic config");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }

    private static void TestReviewClaudeApiProviderRunsAndRecordsTelemetry() {
        var previousUsageDb = Environment.GetEnvironmentVariable("INTELLIGENCEX_USAGE_DB");
        var previousAnthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var tempDir = Path.Combine(Path.GetTempPath(), "ix-review-claude-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "usage.db");

        try {
            using var server = new OpenAiCompatibleTestServer((method, path, body, headers) => {
                AssertEqual("POST", method, "claude review method");
                AssertEqual("/v1/messages", path, "claude review path");
                AssertEqual(true, headers.ContainsKey("x-api-key"), "claude review api key header");
                AssertEqual(true, headers.ContainsKey("anthropic-version"), "claude review version header");
                AssertContainsText(body, "\"model\":\"claude-sonnet-4-5\"", "claude request model");
                AssertContainsText(body, "\"max_tokens\":1024", "claude request max tokens");
                return (200, "OK",
                    "{\"id\":\"msg_review_1\",\"model\":\"claude-sonnet-4-5\",\"content\":[{\"type\":\"text\",\"text\":\"review output\"}],\"usage\":{\"input_tokens\":120,\"output_tokens\":45}}",
                    new Dictionary<string, string> {
                        ["anthropic-organization-id"] = "org-review"
                    });
            });

            Environment.SetEnvironmentVariable("INTELLIGENCEX_USAGE_DB", dbPath);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "test-anthropic-key");

            var settings = new ReviewSettings {
                Provider = ReviewProvider.Claude,
                ProviderHealthChecks = false,
                Preflight = false,
                Model = "claude-sonnet-4-5",
                AnthropicBaseUrl = server.BaseUri.ToString(),
                AnthropicVersion = "2023-06-01",
                AnthropicMaxTokens = 1024,
                AnthropicTimeoutSeconds = 30,
                RetryCount = 1,
                RetryDelaySeconds = 1,
                RetryMaxDelaySeconds = 1,
                FailOpen = false
            };

            var runner = new ReviewRunner(settings);
            var output = runner.RunAsync("Please review this patch.", onPartial: null, updateInterval: null, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            AssertEqual("review output", output, "claude review output");

            using var rootStore = new SqliteSourceRootStore(dbPath);
            using var eventStore = new SqliteUsageEventStore(dbPath);
            var roots = rootStore.GetAll();
            var events = eventStore.GetAll();

            AssertEqual(1, roots.Count, "claude reviewer telemetry root count");
            AssertEqual("claude", roots[0].ProviderId, "claude reviewer telemetry provider");
            AssertEqual(1, events.Count, "claude reviewer telemetry event count");
            AssertEqual("claude", events[0].ProviderId, "claude reviewer telemetry event provider");
            AssertEqual("claude.reviewer-api", events[0].AdapterId, "claude reviewer telemetry adapter");
            AssertEqual("reviewer", events[0].Surface, "claude reviewer telemetry surface");
            AssertEqual("org-review", events[0].ProviderAccountId, "claude reviewer telemetry account");
            AssertEqual(120L, events[0].InputTokens, "claude reviewer telemetry input");
            AssertEqual(45L, events[0].OutputTokens, "claude reviewer telemetry output");
            AssertEqual(165L, events[0].TotalTokens, "claude reviewer telemetry total");
            AssertEqual(UsageTruthLevel.Exact, events[0].TruthLevel, "claude reviewer telemetry truth");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_USAGE_DB", previousUsageDb);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previousAnthropicApiKey);
            try {
                if (Directory.Exists(tempDir)) {
                    Directory.Delete(tempDir, recursive: true);
                }
            } catch {
                // best effort cleanup
            }
        }
    }



    private static void TestReviewOpenAiCompatibleApiKeyEnvWhitespaceFailsFast() {
        var envName = "IX_OPENAI_COMPAT_KEY_TEST";
        var previous = Environment.GetEnvironmentVariable(envName);
        var previousCompat = Environment.GetEnvironmentVariable("OPENAI_COMPATIBLE_API_KEY");
        try {
            Environment.SetEnvironmentVariable(envName, "   ");
            Environment.SetEnvironmentVariable("OPENAI_COMPATIBLE_API_KEY", null);

            var settings = new ReviewSettings {
                Provider = ReviewProvider.OpenAICompatible,
                ProviderHealthChecks = false,
                Preflight = false,
                Model = "test-model",
                OpenAICompatibleBaseUrl = "http://127.0.0.1:12345",
                OpenAICompatibleApiKeyEnv = envName,
                OpenAICompatibleApiKey = string.Empty,
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
                AssertEqual(true, false, "openai-compatible should throw when apiKeyEnv resolves to whitespace");
            } catch (InvalidOperationException ex) {
                AssertContainsText(ex.Message, envName, "openai-compatible api key error mentions env var name");
                AssertContainsText(ex.Message, "empty", "openai-compatible api key error indicates empty value");
            }
        } finally {
            Environment.SetEnvironmentVariable(envName, previous);
            Environment.SetEnvironmentVariable("OPENAI_COMPATIBLE_API_KEY", previousCompat);
        }
    }
    private static void TestReviewOpenAiCompatiblePreflightTreats405AsReachable() {
        using var server = new OpenAiCompatibleTestServer((method, path, _, _) => {
            if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/", StringComparison.OrdinalIgnoreCase)) {
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
        using var server = new OpenAiCompatibleTestServer((method, path, body, _) => {
            if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/", StringComparison.OrdinalIgnoreCase)) {
                return (302, "Found", "{}", new Dictionary<string, string> {
                    ["Location"] = "/preflight-redirected"
                });
            }
            if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/preflight-redirected", StringComparison.OrdinalIgnoreCase)) {
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
                // Ensure redirect replays the POST body (gateways commonly redirect with 302 and still expect POST).
                if (string.IsNullOrWhiteSpace(body)) {
                    return (400, "Bad Request", "{\"error\":\"expected POST body\"}", null);
                }
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

    private static void TestReviewOpenAiCompatibleRedirect303SwitchesToGet() {
        using var server = new OpenAiCompatibleTestServer((method, path, body, _) => {
            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/chat/completions", StringComparison.OrdinalIgnoreCase)) {
                return (303, "See Other", "{}", new Dictionary<string, string> {
                    ["Location"] = "/v1/chat/completions-redirected"
                });
            }
            if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/chat/completions-redirected", StringComparison.OrdinalIgnoreCase)) {
                // Ensure we did not forward the original POST body.
                if (!string.IsNullOrEmpty(body)) {
                    return (400, "Bad Request", "{\"error\":\"expected empty body\"}", null);
                }
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
        AssertEqual("ok", result, "openai-compatible 303 redirect switches POST to GET");
    }


    private static void TestReviewOpenAiCompatibleRedirect303KeepsGetForRedirectChain() {
        using var server = new OpenAiCompatibleTestServer((method, path, body, _) => {
            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/chat/completions", StringComparison.OrdinalIgnoreCase)) {
                return (303, "See Other", "{}", new Dictionary<string, string> {
                    ["Location"] = "/v1/chat/completions-redirected"
                });
            }
            if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/chat/completions-redirected", StringComparison.OrdinalIgnoreCase)) {
                return (302, "Found", "{}", new Dictionary<string, string> {
                    ["Location"] = "/v1/chat/completions-final"
                });
            }
            if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/chat/completions-final", StringComparison.OrdinalIgnoreCase)) {
                // After a 303, the entire redirect chain must stay GET and never replay the original POST body.
                if (!string.IsNullOrEmpty(body)) {
                    return (400, "Bad Request", "{\"error\":\"expected empty body\"}", null);
                }
                return (200, "OK", "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}", null);
            }
            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                path.Equals("/v1/chat/completions-final", StringComparison.OrdinalIgnoreCase)) {
                return (400, "Bad Request", "{\"error\":\"unexpected POST after 303\"}", null);
            }
            return (400, "Bad Request", "{\"error\":\"unexpected request\"}", null);
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
        AssertEqual("ok", result, "openai-compatible 303 keeps GET for entire redirect chain");
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
