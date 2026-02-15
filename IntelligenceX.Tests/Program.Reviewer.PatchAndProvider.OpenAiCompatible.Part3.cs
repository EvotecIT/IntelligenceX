namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
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

    private static void TestReviewOpenAiCompatibleRejectsHttpNonLoopbackWithoutExplicitOverride() {
        var settings = new ReviewSettings {
            Provider = ReviewProvider.OpenAICompatible,
            Model = "test-model",
            OpenAICompatibleBaseUrl = "http://example.com",
            OpenAICompatibleApiKey = "test",
            OpenAICompatibleAllowInsecureHttp = true,
            OpenAICompatibleAllowInsecureHttpNonLoopback = false,
            RetryCount = 1,
            RetryDelaySeconds = 1,
            RetryMaxDelaySeconds = 1,
            ProviderHealthChecks = false,
            Preflight = false,
            FailOpen = false,
            Diagnostics = false
        };

        try {
            var runner = new ReviewRunner(settings);
            runner.RunAsync("hi", onPartial: null, updateInterval: null, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            AssertEqual(true, false, "openai-compatible should reject http non-loopback without explicit non-loopback override");
        } catch (InvalidOperationException ex) {
            AssertContainsText(ex.Message, "allowInsecureHttpNonLoopback", "openai-compatible http non-loopback requires explicit non-loopback override");
        }
    }
}
#endif
