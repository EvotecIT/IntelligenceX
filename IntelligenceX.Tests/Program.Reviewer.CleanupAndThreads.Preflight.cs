namespace IntelligenceX.Tests;

internal static partial class Program {
    #if INTELLIGENCEX_REVIEWER
    private static void TestPreflightSocketFailure() {
        var options = new OpenAINativeOptions {
            ChatGptApiBaseUrl = "http://127.0.0.1:1"
        };
        static bool IsExpectedSocketFailureMessage(string message) {
            return message.Contains("Connectivity preflight failed", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("Connectivity preflight timed out", StringComparison.OrdinalIgnoreCase);
        }
        try {
            CallPreflightNativeConnectivity(options, TimeSpan.FromSeconds(1));
            throw new InvalidOperationException("Expected socket failure.");
        } catch (InvalidOperationException ex) {
            var message = ex.Message ?? string.Empty;
            var isSocketFailure = IsExpectedSocketFailureMessage(message);
            AssertEqual(true, isSocketFailure, "preflight socket failure");
        } catch (TimeoutException ex) {
            var message = ex.Message ?? string.Empty;
            var isSocketFailure = IsExpectedSocketFailureMessage(message);
            AssertEqual(true, isSocketFailure, "preflight socket failure timeout");
        }
    }

    private static void TestPreflightAuthStatusesAreReachable() {
        var statuses = new[] {
            (Code: 401, Reason: "Unauthorized"),
            (Code: 403, Reason: "Forbidden")
        };
        foreach (var status in statuses) {
            using var server = new LocalHttpServer(_ => new HttpResponse("{}", null, status.Code, status.Reason));
            var options = new OpenAINativeOptions {
                ChatGptApiBaseUrl = server.BaseUri.ToString().TrimEnd('/')
            };
            CallPreflightNativeConnectivity(options, TimeSpan.FromSeconds(1));
        }
    }

    private static void TestPreflightNonSuccessStatus() {
        using var server = new LocalHttpServer(_ => new HttpResponse("{}", null, 500, "Server Error"));
        var options = new OpenAINativeOptions {
            ChatGptApiBaseUrl = server.BaseUri.ToString().TrimEnd('/')
        };
        try {
            CallPreflightNativeConnectivity(options, TimeSpan.FromSeconds(1));
            throw new InvalidOperationException("Expected non-success status.");
        } catch (HttpRequestException ex) {
            AssertContainsText(ex.Message, "HTTP 500", "preflight non-2xx");
        }
    }

    private static void TestPreflightDnsFailureMapping() {
        var httpException = new HttpRequestException("dns failed", new SocketException((int)SocketError.HostNotFound));
        var mapped = CallMapPreflightConnectivityException(httpException, "example.invalid", TimeSpan.FromSeconds(1), false);
        AssertNotNull(mapped, "preflight dns mapped");
        AssertEqual(true, mapped is InvalidOperationException, "preflight dns mapped type");
        AssertContainsText(mapped!.Message, "DNS resolution", "preflight dns mapped message");
    }

    private static void TestPreflightSocketFailureMapping() {
        var httpException = new HttpRequestException("connect failed", new SocketException((int)SocketError.ConnectionRefused));
        var mapped = CallMapPreflightConnectivityException(httpException, "example.invalid", TimeSpan.FromSeconds(1), false);
        AssertNotNull(mapped, "preflight socket mapped");
        AssertEqual(true, mapped is InvalidOperationException, "preflight socket mapped type");
        AssertContainsText(mapped!.Message, "network connectivity", "preflight socket mapped message");
    }

    private static void TestPreflightHttpStatusMappingBypass() {
        var httpException = new HttpRequestException("bad request", null, HttpStatusCode.BadRequest);
        var mapped = CallMapPreflightConnectivityException(httpException, "example.invalid", TimeSpan.FromSeconds(1), false);
        AssertEqual<Exception?>(null, mapped, "preflight status mapping bypass");
    }

    private static void TestPreflightCancellationRequestedMappingBypass() {
        var httpException = new HttpRequestException("cancelled", new TaskCanceledException("cancelled"));
        var mapped = CallMapPreflightConnectivityException(httpException, "example.invalid", TimeSpan.FromSeconds(1), true);
        AssertEqual<Exception?>(null, mapped, "preflight cancellation mapping bypass");
    }

    private static void TestReviewConfigValidatorAllowsAdditionalProperties() {
        var result = RunConfigValidation("{\"review\":{\"extraSetting\":true}}");
        AssertEqual(true, result is not null, "validator result");
        AssertEqual(0, result!.Warnings.Count, "additional properties should not warn");
        AssertEqual(0, result.Errors.Count, "additional properties should not error");
    }

    private static void TestReviewConfigValidatorInvalidEnum() {
        var result = RunConfigValidation("{\"review\":{\"length\":\"SHORT\"}}");
        AssertEqual(true, result is not null, "validator result");
        AssertEqual(true, result!.Errors.Count > 0, "invalid enum should error");
    }
    #endif
}
