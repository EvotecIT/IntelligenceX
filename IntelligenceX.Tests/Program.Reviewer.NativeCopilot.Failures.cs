#if INTELLIGENCEX_REVIEWER
namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestNativeCopilotHttpStatusControlsReviewerRetryClassification() {
        foreach (int code in new[] { 400, 401, 429, 503 }) {
            using var client = IntelligenceXClient.ConnectAsync(new IntelligenceXClientOptions {
                TransportKind = OpenAITransportKind.CopilotNative, DefaultModel = "model",
                CopilotOptions = new() { GitHubToken = "host-token", HttpMessageHandler = new NativeCopilotHealthHandler((_, _) =>
                    Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)code))) }
            }).GetAwaiter().GetResult();
            Exception? failure = null;
            try { client.ListModelsAsync().GetAwaiter().GetResult(); }
            catch (Exception error) { failure = error; }
            if (failure is null) throw new Exception("The failed provider request unexpectedly succeeded.");
            var classified = ReviewDiagnostics.Classify(failure);
            AssertEqual(code >= 429, classified.IsTransient, "rate limits and service failures are transient");
            AssertEqual(code switch {
                400 => ReviewDiagnostics.ReviewErrorCategory.Config,
                401 => ReviewDiagnostics.ReviewErrorCategory.Auth,
                429 => ReviewDiagnostics.ReviewErrorCategory.RateLimit,
                _ => ReviewDiagnostics.ReviewErrorCategory.ServiceUnavailable
            }, classified.Category, "provider status classification");
        }
    }

    private static void TestNativeCopilotHealthDeadlineIsTransientButCallerCancellationIsNot() {
        foreach (bool callerCancellation in new[] { false, true }) {
            using var cancellation = new CancellationTokenSource();
            using var handler = new NativeCopilotHealthHandler(async (_, token) => {
                if (callerCancellation) cancellation.Cancel();
                await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("The health probe must be canceled.");
            });
            var options = new IntelligenceXClientOptions { TransportKind = OpenAITransportKind.CopilotNative,
                CopilotOptions = new() { GitHubToken = "host-token", HttpMessageHandler = handler, RequestTimeout = TimeSpan.FromSeconds(30) } };
            Exception? failure = null;
            try { ReviewRunner.RunCopilotHealthCheckAsync(options, TimeSpan.FromMilliseconds(100), cancellation.Token).GetAwaiter().GetResult(); }
            catch (Exception error) { failure = error; }
            if (failure is null) throw new Exception("The stalled health check unexpectedly succeeded.");
            var classification = ReviewDiagnostics.Classify(failure);
            AssertEqual(callerCancellation ? ReviewDiagnostics.ReviewErrorCategory.Cancelled : ReviewDiagnostics.ReviewErrorCategory.Timeout,
                classification.Category, "health deadline/caller classification");
            AssertEqual(!callerCancellation, classification.IsTransient, "health retry and fail-open eligibility");
        }
    }

    private static void TestNativeCopilotEnvironmentCredentialOverridesConfiguredToken() {
        const string input = "INPUT_COPILOT_TOKEN_ENV", environment = "COPILOT_TOKEN_ENV", tokenName = "IX_TEST_NATIVE_CREDENTIAL";
        string? priorInput = Environment.GetEnvironmentVariable(input), priorEnvironment = Environment.GetEnvironmentVariable(environment),
            priorToken = Environment.GetEnvironmentVariable(tokenName);
        try {
            foreach (bool useInput in new[] { false, true }) {
                Environment.SetEnvironmentVariable(input, useInput ? tokenName : null);
                Environment.SetEnvironmentVariable(environment, useInput ? "LOWER_PRECEDENCE" : tokenName);
                Environment.SetEnvironmentVariable(tokenName, "replacement-token");
                var settings = new ReviewSettings { CopilotModel = "model", CopilotToken = "configured-token" };
                ReviewSettings.ApplyEnvironment(settings);
                var options = new ReviewRunner(settings).BuildCopilotClientOptionsForTests().CopilotOptions;
                AssertEqual<string?>(null, options.GitHubToken, "environment override clears configured literal");
                AssertEqual("replacement-token", options.TokenProvider!(default).GetAwaiter().GetResult(), "override credential reaches SDK");
            }
        } finally {
            Environment.SetEnvironmentVariable(input, priorInput);
            Environment.SetEnvironmentVariable(environment, priorEnvironment);
            Environment.SetEnvironmentVariable(tokenName, priorToken);
        }
    }

    private sealed class NativeCopilotHealthHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
#endif
