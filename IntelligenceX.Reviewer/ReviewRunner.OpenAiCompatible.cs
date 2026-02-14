using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewRunner {
    private const int OpenAiCompatibleMaxRedirects = 5;

    private async Task RunOpenAiCompatiblePreflightAsync(TimeSpan timeout, CancellationToken cancellationToken) {
        var endpoint = ResolveOpenAiCompatibleModelsEndpoint(_settings.OpenAICompatibleBaseUrl, _settings.OpenAICompatibleAllowInsecureHttp);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        // Treat any HTTP response as "reachable" (including 401/403/404/405). This is a connectivity check, not auth validation.
        try {
            using var response = await SendOpenAiCompatibleWithRedirectsAsync(
                    uri => new HttpRequestMessage(HttpMethod.Get, uri),
                    endpoint,
                    timeoutCts.Token).ConfigureAwait(false);

            if (_settings.Diagnostics) {
                Console.Error.WriteLine(
                    $"Connectivity preflight returned HTTP {(int)response.StatusCode} for {endpoint.Host} (reachable).");
            }
        } catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
            throw new TimeoutException($"Connectivity preflight timed out after {timeout.TotalSeconds:0.#}s for {endpoint.Host}.", ex);
        } catch (HttpRequestException ex) {
            // In some environments HttpClient may throw HttpRequestException with StatusCode; treat that as "reachable".
            if (ex.StatusCode.HasValue) {
                if (_settings.Diagnostics) {
                    Console.Error.WriteLine(
                        $"Connectivity preflight returned HTTP {(int)ex.StatusCode.Value} for {endpoint.Host} (reachable).");
                }
                return;
            }
            throw new InvalidOperationException(
                $"Connectivity preflight failed for {endpoint.Host}. Check URL, DNS, proxy, and network settings.", ex);
        }
    }

    private async Task<string> RunOpenAiCompatibleWithRetryAsync(string prompt, CancellationToken cancellationToken) {
        var retryState = new ReviewRetryState();
        try {
            if (_settings.Preflight && !_settings.ProviderHealthChecks) {
                var timeout = _settings.PreflightTimeoutSeconds > 0
                    ? TimeSpan.FromSeconds(_settings.PreflightTimeoutSeconds)
                    : TimeSpan.FromSeconds(15);
                await RunOpenAiCompatiblePreflightAsync(timeout, cancellationToken).ConfigureAwait(false);
            }

            return await ReviewRetryPolicy.RunAsync(
                    () => RunOpenAiCompatibleOnceAsync(prompt, cancellationToken),
                    IsTransient,
                    _settings.RetryCount,
                    _settings.RetryDelaySeconds,
                    _settings.RetryMaxDelaySeconds,
                    Math.Max(1.0, _settings.RetryBackoffMultiplier),
                    Math.Max(0, _settings.RetryJitterMinMs),
                    Math.Max(0, _settings.RetryJitterMaxMs),
                    cancellationToken,
                    ex => ReviewDiagnostics.FormatExceptionSummary(ex, _settings.Diagnostics),
                    extraAttempts: 0,
                    extraRetryPredicate: null,
                    retryState,
                    operationName: "OpenAI-compatible").ConfigureAwait(false);
        } catch (Exception ex) {
            ReviewDiagnostics.LogFailure(ex, _settings, snapshot: null, retryState);
            if (ShouldFailOpen(_settings, ex)) {
                return ReviewDiagnostics.BuildFailureBody(ex, _settings, snapshot: null, retryState);
            }
            throw;
        }
    }

    private async Task<string> RunOpenAiCompatibleOnceAsync(string prompt, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(_settings.Model)) {
            throw new InvalidOperationException("OpenAI-compatible provider requires review.model to be set.");
        }
        var endpoint = ResolveOpenAiCompatibleEndpoint(_settings.OpenAICompatibleBaseUrl, _settings.OpenAICompatibleAllowInsecureHttp);
        var apiKey = ResolveOpenAiCompatibleApiKey();

        var timeoutSeconds = Math.Max(1, _settings.OpenAICompatibleTimeoutSeconds);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var payload = new {
            model = _settings.Model,
            messages = new[] {
                new { role = "user", content = prompt }
            }
        };
        var payloadJson = JsonSerializer.Serialize(payload);

        using var response = await SendOpenAiCompatibleWithRedirectsAsync(uri => {
                var request = new HttpRequestMessage(HttpMethod.Post, uri);
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
                request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
                return request;
            }, endpoint, timeoutCts.Token)
            .ConfigureAwait(false);

        var responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) {
            var code = (int)response.StatusCode;
            var body = FormatOpenAiCompatibleErrorBody(responseText, apiKey, _settings.Diagnostics);
            throw new InvalidOperationException($"OpenAI-compatible request failed (HTTP {code}). {body}");
        }

        return ExtractOpenAiCompatibleOutput(responseText);
    }

    private async Task<HttpResponseMessage> SendOpenAiCompatibleWithRedirectsAsync(
        Func<Uri, HttpRequestMessage> createRequest,
        Uri endpoint,
        CancellationToken cancellationToken) {
        var current = endpoint;
        for (var redirect = 0; redirect <= OpenAiCompatibleMaxRedirects; redirect++) {
            HttpResponseMessage response;
            using (var request = createRequest(current)) {
                response = await OpenAiCompatibleHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!IsRedirectStatusCode(response.StatusCode)) {
                return response;
            }

            if (redirect == OpenAiCompatibleMaxRedirects) {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new InvalidOperationException(
                    $"OpenAI-compatible request failed: too many redirects (last HTTP {status}). Check your baseUrl/proxy configuration.");
            }

            var location = response.Headers.Location;
            if (location is null) {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new InvalidOperationException(
                    $"OpenAI-compatible request failed: redirect (HTTP {status}) without Location header.");
            }

            var next = ResolveRedirectUri(current, location);
            ValidateOpenAiCompatibleRedirectUri(next, _settings.OpenAICompatibleAllowInsecureHttp);

            if (_settings.Diagnostics) {
                Console.Error.WriteLine(
                    $"OpenAI-compatible redirect {(int)response.StatusCode}: {current} -> {next}");
            }

            response.Dispose();
            current = next;
        }

        throw new InvalidOperationException("OpenAI-compatible redirect loop ended unexpectedly.");
    }

    private static bool IsRedirectStatusCode(HttpStatusCode status) {
        return status == HttpStatusCode.MovedPermanently ||
               status == HttpStatusCode.Found ||
               status == HttpStatusCode.SeeOther ||
               status == HttpStatusCode.TemporaryRedirect ||
               status == HttpStatusCode.PermanentRedirect;
    }

    private static Uri ResolveRedirectUri(Uri requestUri, Uri location) {
        if (location.IsAbsoluteUri) {
            return location;
        }
        return new Uri(requestUri, location);
    }

    private static void ValidateOpenAiCompatibleRedirectUri(Uri uri, bool allowInsecureHttp) {
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"OpenAI-compatible redirect must use http:// or https:// (got '{uri.Scheme}').");
        }
        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !allowInsecureHttp &&
            !uri.IsLoopback) {
            throw new InvalidOperationException(
                "OpenAI-compatible redirect attempted to use http:// for a non-loopback host. " +
                "Set review.openaiCompatible.allowInsecureHttp=true to override.");
        }
    }

    private static Uri ResolveOpenAiCompatibleBaseUri(string? baseUrl, bool allowInsecureHttp) {
        var trimmed = (baseUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) {
            throw new InvalidOperationException("OpenAI-compatible provider requires review.openaiCompatible.baseUrl.");
        }
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var baseUri)) {
            throw new InvalidOperationException($"OpenAI-compatible baseUrl is invalid: '{trimmed}'.");
        }
        if (!baseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !baseUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"OpenAI-compatible baseUrl must use http:// or https:// (got '{baseUri.Scheme}').");
        }
        if (baseUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !allowInsecureHttp &&
            !baseUri.IsLoopback) {
            throw new InvalidOperationException(
                "OpenAI-compatible baseUrl must use https:// for non-loopback hosts. " +
                "Set review.openaiCompatible.allowInsecureHttp=true (or OPENAI_COMPATIBLE_ALLOW_INSECURE_HTTP=1) to override.");
        }
        return baseUri;
    }

    private static Uri ResolveOpenAiCompatibleEndpoint(string? baseUrl, bool allowInsecureHttp) {
        var baseUri = ResolveOpenAiCompatibleBaseUri(baseUrl, allowInsecureHttp);
        var normalized = baseUri.ToString().TrimEnd('/');
        if (normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) {
            normalized += "/chat/completions";
        } else {
            normalized += "/v1/chat/completions";
        }
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var endpoint)) {
            throw new InvalidOperationException($"OpenAI-compatible endpoint is invalid: '{normalized}'.");
        }
        return endpoint;
    }

    private static Uri ResolveOpenAiCompatibleModelsEndpoint(string? baseUrl, bool allowInsecureHttp) {
        var baseUri = ResolveOpenAiCompatibleBaseUri(baseUrl, allowInsecureHttp);
        var normalized = baseUri.ToString().TrimEnd('/');
        if (normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) {
            normalized += "/models";
        } else {
            normalized += "/v1/models";
        }
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var endpoint)) {
            throw new InvalidOperationException($"OpenAI-compatible models endpoint is invalid: '{normalized}'.");
        }
        return endpoint;
    }

    private string ResolveOpenAiCompatibleApiKey() {
        var envName = _settings.OpenAICompatibleApiKeyEnv?.Trim();
        if (!string.IsNullOrWhiteSpace(envName)) {
            var value = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(value)) {
                SecretsAudit.Record($"OpenAI-compatible API key from {envName}");
                return value.Trim();
            }
        }

        var envValue = Environment.GetEnvironmentVariable("OPENAI_COMPATIBLE_API_KEY");
        if (!string.IsNullOrWhiteSpace(envValue)) {
            SecretsAudit.Record("OpenAI-compatible API key from OPENAI_COMPATIBLE_API_KEY");
            return envValue.Trim();
        }

        var configValue = _settings.OpenAICompatibleApiKey;
        if (!string.IsNullOrWhiteSpace(configValue)) {
            SecretsAudit.Record("OpenAI-compatible API key from config (openaiCompatible.apiKey)");
            return configValue.Trim();
        }

        throw new InvalidOperationException(
            "OpenAI-compatible provider requires an API key. Set review.openaiCompatible.apiKeyEnv or OPENAI_COMPATIBLE_API_KEY.");
    }

    private static string ExtractOpenAiCompatibleOutput(string? responseText) {
        if (string.IsNullOrWhiteSpace(responseText)) {
            throw new InvalidOperationException("OpenAI-compatible response was empty.");
        }

        try {
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == System.Text.Json.JsonValueKind.Array &&
                choices.GetArrayLength() > 0) {
                var first = choices[0];
                if (first.TryGetProperty("message", out var message) &&
                    message.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    message.TryGetProperty("content", out var content) &&
                    content.ValueKind == System.Text.Json.JsonValueKind.String) {
                    return content.GetString() ?? string.Empty;
                }
                if (first.TryGetProperty("text", out var text) && text.ValueKind == System.Text.Json.JsonValueKind.String) {
                    return text.GetString() ?? string.Empty;
                }
            }

            throw new InvalidOperationException("OpenAI-compatible response JSON missing choices[0].message.content.");
        } catch (JsonException ex) {
            throw new InvalidOperationException("OpenAI-compatible response was not valid JSON.", ex);
        }
    }

    private static readonly Regex BearerTokenRegex =
        new("Bearer\\s+[A-Za-z0-9._\\-]{8,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SensitiveJsonFieldRegex =
        new("\"(?:(?:api[_-]?key)|authorization)\"\\s*:\\s*\"[^\"]+\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string FormatOpenAiCompatibleErrorBody(string? content, string? apiKey, bool diagnostics) {
        if (!diagnostics) {
            return "Response body omitted (set review.diagnostics=true to include sanitized output).";
        }
        var sanitized = SanitizeErrorContent(content);
        if (!string.IsNullOrWhiteSpace(apiKey)) {
            sanitized = sanitized.Replace(apiKey.Trim(), "[REDACTED]");
        }
        sanitized = BearerTokenRegex.Replace(sanitized, "Bearer [REDACTED]");
        sanitized = SensitiveJsonFieldRegex.Replace(sanitized, m => {
            var idx = m.Value.IndexOf(':');
            if (idx < 0) {
                return "\"[REDACTED]\":\"[REDACTED]\"";
            }
            return m.Value.Substring(0, idx) + ":\"[REDACTED]\"";
        });
        return sanitized;
    }

    private static string SanitizeErrorContent(string? content) {
        var text = (content ?? string.Empty)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
        if (string.IsNullOrWhiteSpace(text)) {
            return "Response body was empty.";
        }
        if (text.Length > 240) {
            text = text.Substring(0, 240) + "...";
        }
        return "Body: " + text;
    }
}
