using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using STJ = System.Text.Json.Nodes;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewRunner {
    private const int OpenAiCompatibleMaxRedirects = 5;
    private sealed record OpenAiCompatibleRawResponse(HttpStatusCode StatusCode, string Body);

    private async Task RunOpenAiCompatiblePreflightAsync(TimeSpan timeout, CancellationToken cancellationToken) {
        var endpoint = ResolveOpenAiCompatiblePreflightEndpoint(_settings.OpenAICompatibleBaseUrl, _settings.OpenAICompatibleAllowInsecureHttp, _settings.OpenAICompatibleAllowInsecureHttpNonLoopback);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        // Treat any HTTP response as "reachable" (including 401/403/404/405). This is a connectivity check, not auth validation.
        try {
            var response = await SendOpenAiCompatibleWithRedirectsAsync(
                    uri => new HttpRequestMessage(HttpMethod.Get, uri),
                    null,
                    endpoint,
                    readBodyOnSuccess: false,
                    readBodyOnError: false,
                    timeoutCts.Token).ConfigureAwait(false);
            if (_settings.Diagnostics) {
                Console.Error.WriteLine(
                    $"Connectivity preflight returned HTTP {(int)response.StatusCode} for {endpoint.Host} (reachable).");
            }
        } catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
            throw new TimeoutException(
                $"Connectivity preflight timed out after {timeout.TotalSeconds:0.#}s for {endpoint.Host}. " +
                "If your gateway blocks connectivity probes (or requires auth), set review.preflight=false or enable review.providerHealthChecks=true.",
                ex);
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
                    extraAttempts: _settings.RetryExtraOnResponseEnded ? 1 : 0,
                    extraRetryPredicate: ReviewDiagnostics.IsResponseEnded,
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
        var endpoint = ResolveOpenAiCompatibleEndpoint(_settings.OpenAICompatibleBaseUrl, _settings.OpenAICompatibleAllowInsecureHttp, _settings.OpenAICompatibleAllowInsecureHttpNonLoopback);
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

        var response = await SendOpenAiCompatibleWithRedirectsAsync(uri => {
                var request = new HttpRequestMessage(HttpMethod.Post, uri);
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
                return request;
            }, () => new StringContent(payloadJson, Encoding.UTF8, "application/json"), endpoint, readBodyOnSuccess: true, readBodyOnError: _settings.Diagnostics, timeoutCts.Token)
            .ConfigureAwait(false);

        var code = (int)response.StatusCode;
        var isSuccess = code >= 200 && code <= 299;
        var responseText = response.Body;

        if (!isSuccess) {
            var body = FormatOpenAiCompatibleErrorBody(responseText, apiKey, _settings.Diagnostics);
            throw new InvalidOperationException($"OpenAI-compatible request failed (HTTP {code}). {body}");
        }

        return ExtractOpenAiCompatibleOutput(responseText);
    }

    private async Task<OpenAiCompatibleRawResponse> SendOpenAiCompatibleWithRedirectsAsync(
        Func<Uri, HttpRequestMessage> createRequest,
        Func<HttpContent?>? createContent,
        Uri endpoint,
        bool readBodyOnSuccess,
        bool readBodyOnError,
        CancellationToken cancellationToken) {
        var current = endpoint;
        Uri? redirectedFrom = null;
        // RFC 9110: after a 303 See Other we must switch to GET and keep it for the remainder of the redirect chain.
        HttpMethod? redirectMethodOverride = null;
        for (var redirect = 0; redirect <= OpenAiCompatibleMaxRedirects; redirect++) {
            using var request = createRequest(current);
            if (redirectMethodOverride is not null) {
                request.Method = redirectMethodOverride;
                if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head) {
                    // Ensure we never replay a request body after 303.
                    request.Content = null;
                }
            }
            if (createContent is not null && request.Method != HttpMethod.Get && request.Method != HttpMethod.Head) {
                request.Content = createContent();
            }
            if (_settings.OpenAICompatibleDropAuthorizationOnRedirect && redirectedFrom is not null) {
                var sameAuthority = string.Equals(redirectedFrom.Scheme, current.Scheme, StringComparison.OrdinalIgnoreCase) &&
                                   string.Equals(redirectedFrom.Host, current.Host, StringComparison.OrdinalIgnoreCase) &&
                                   redirectedFrom.Port == current.Port;
                if (!sameAuthority) {
                    request.Headers.Remove("Authorization");
                    if (_settings.Diagnostics) {
                        Console.Error.WriteLine("OpenAI-compatible redirect: dropped Authorization header.");
                    }
                }
                redirectedFrom = null;
            }

            using var response = await OpenAiCompatibleHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!IsRedirectStatusCode(response.StatusCode)) {
                var shouldReadBody = response.IsSuccessStatusCode ? readBodyOnSuccess : readBodyOnError;
                var body = string.Empty;
                if (shouldReadBody) {
                    body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                } else {
                    await DrainOpenAiCompatibleBodyAsync(response, cancellationToken).ConfigureAwait(false);
                }
                return new OpenAiCompatibleRawResponse(response.StatusCode, body);
            }

            if (redirect == OpenAiCompatibleMaxRedirects) {
                var status = (int)response.StatusCode;
                throw new InvalidOperationException(
                    $"OpenAI-compatible request failed: too many redirects (last HTTP {status}). Check your baseUrl/proxy configuration.");
            }

            var location = response.Headers.Location;
            if (location is null) {
                var status = (int)response.StatusCode;
                throw new InvalidOperationException(
                    $"OpenAI-compatible request failed: redirect (HTTP {status}) without Location header.");
            }

            await DrainOpenAiCompatibleBodyAsync(response, cancellationToken).ConfigureAwait(false);
            var next = ResolveRedirectUri(current, location);
            ValidateOpenAiCompatibleRedirectUri(current, next, _settings.OpenAICompatibleAllowInsecureHttp, _settings.OpenAICompatibleAllowInsecureHttpNonLoopback);

            if (_settings.Diagnostics) {
                Console.Error.WriteLine(
                    $"OpenAI-compatible redirect {(int)response.StatusCode}: {current} -> {next}");
            }
            if (response.StatusCode == HttpStatusCode.SeeOther) {
                redirectMethodOverride = HttpMethod.Get;
            }

            redirectedFrom = current;
            current = next;
        }

        throw new InvalidOperationException("OpenAI-compatible redirect loop ended unexpectedly.");
    }

    private const int OpenAiCompatibleMaxDrainBytes = 256 * 1024;

    private static async Task DrainOpenAiCompatibleBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
        if (response.Content is null) {
            return;
        }
        try {
            var length = response.Content.Headers.ContentLength;
            if (length.HasValue && length.Value > OpenAiCompatibleMaxDrainBytes) {
                return;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[8192];
            var remaining = OpenAiCompatibleMaxDrainBytes;
            while (remaining > 0) {
                var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken)
                    .ConfigureAwait(false);
                if (read <= 0) {
                    break;
                }
                remaining -= read;
            }
        } catch {
            // Ignore drain failures; response disposal will still release resources.
        }
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

    private static void ValidateOpenAiCompatibleRedirectUri(Uri from, Uri to, bool allowInsecureHttp, bool allowInsecureHttpNonLoopback) {
        if (!to.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !to.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"OpenAI-compatible redirect must use http:// or https:// (got '{to.Scheme}').");
        }

        // Redirects should be predictable; don't allow jumping to a different host by default.
        if (!string.Equals(from.Host, to.Host, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"OpenAI-compatible redirect to a different host is not allowed ({from.Host} -> {to.Host}). " +
                "Update the baseUrl to the final host instead.");
        }

        var fromIsHttps = from.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var toIsHttp = to.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!toIsHttp) {
            return;
        }
        if (!allowInsecureHttp) {
            // Prevent accidental scheme downgrade even on loopback unless explicitly allowed.
            if (fromIsHttps) {
                throw new InvalidOperationException(
                    "OpenAI-compatible redirect attempted to downgrade from https:// to http://. " +
                    "Set review.openaiCompatible.allowInsecureHttp=true to override.");
            }
            if (!to.IsLoopback) {
                throw new InvalidOperationException(
                    "OpenAI-compatible redirect attempted to use http:// for a non-loopback host. " +
                    "Set review.openaiCompatible.allowInsecureHttp=true to override.");
            }
            return;
        }
        if (!to.IsLoopback && !allowInsecureHttpNonLoopback) {
            throw new InvalidOperationException(
                "OpenAI-compatible redirect attempted to use http:// for a non-loopback host. " +
                "Set review.openaiCompatible.allowInsecureHttpNonLoopback=true to acknowledge the risk.");
        }
    }

    private static Uri ResolveOpenAiCompatibleBaseUri(string? baseUrl, bool allowInsecureHttp, bool allowInsecureHttpNonLoopback) {
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
            !baseUri.IsLoopback) {
            if (!allowInsecureHttp) {
                throw new InvalidOperationException(
                    "OpenAI-compatible baseUrl must use https:// for non-loopback hosts. " +
                    "Set review.openaiCompatible.allowInsecureHttp=true (or OPENAI_COMPATIBLE_ALLOW_INSECURE_HTTP=1) to override.");
            }
            if (!allowInsecureHttpNonLoopback) {
                throw new InvalidOperationException(
                    "OpenAI-compatible baseUrl must use https:// for non-loopback hosts. " +
                    "To override, set review.openaiCompatible.allowInsecureHttpNonLoopback=true (or OPENAI_COMPATIBLE_ALLOW_INSECURE_HTTP_NON_LOOPBACK=1).");
            }
        }
        return baseUri;
    }

    private static Uri ResolveOpenAiCompatibleEndpoint(string? baseUrl, bool allowInsecureHttp, bool allowInsecureHttpNonLoopback) {
        var baseUri = ResolveOpenAiCompatibleBaseUri(baseUrl, allowInsecureHttp, allowInsecureHttpNonLoopback);
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

    private static Uri ResolveOpenAiCompatiblePreflightEndpoint(string? baseUrl, bool allowInsecureHttp, bool allowInsecureHttpNonLoopback) {
        var baseUri = ResolveOpenAiCompatibleBaseUri(baseUrl, allowInsecureHttp, allowInsecureHttpNonLoopback);
        // Probe authority only; avoid provider-specific endpoints (some gateways block /v1/models, etc.).
        return new Uri(baseUri, "/");
    }

    private string ResolveOpenAiCompatibleApiKey() {
        var configuredEnvName = (_settings.OpenAICompatibleApiKeyEnv ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(configuredEnvName)) {
            var value = (Environment.GetEnvironmentVariable(configuredEnvName) ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(value)) {
                SecretsAudit.Record($"OpenAI-compatible API key from {configuredEnvName}");
                return value;
            }
        }

        var compatValue = (Environment.GetEnvironmentVariable("OPENAI_COMPATIBLE_API_KEY") ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(compatValue)) {
            SecretsAudit.Record("OpenAI-compatible API key from OPENAI_COMPATIBLE_API_KEY");
            return compatValue;
        }

        var configValue = (_settings.OpenAICompatibleApiKey ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(configValue)) {
            SecretsAudit.Record("OpenAI-compatible API key from config (openaiCompatible.apiKey)");
            return configValue;
        }

        if (!string.IsNullOrWhiteSpace(configuredEnvName)) {
            throw new InvalidOperationException(
                $"OpenAI-compatible provider requires an API key. review.openaiCompatible.apiKeyEnv is set to \"{configuredEnvName}\", but {configuredEnvName} is empty. Set {configuredEnvName}, or set review.openaiCompatible.apiKey, or OPENAI_COMPATIBLE_API_KEY.");
        }

        throw new InvalidOperationException(
            "OpenAI-compatible provider requires an API key. Set review.openaiCompatible.apiKeyEnv, review.openaiCompatible.apiKey, or OPENAI_COMPATIBLE_API_KEY.");
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
        if (string.IsNullOrWhiteSpace(content)) {
            return "Response body was empty.";
        }

        var sanitized = content.Trim();
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
        if (TrySanitizeOpenAiCompatibleErrorBodyJson(sanitized, apiKey, out var sanitizedJson)) {
            sanitized = sanitizedJson;
        }
        return SanitizeErrorContent(sanitized);
    }

    private static string SanitizeErrorContent(string content) {
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

    private static bool TrySanitizeOpenAiCompatibleErrorBodyJson(string content, string? apiKey, out string sanitized) {
        sanitized = string.Empty;
        var trimmed = (content ?? string.Empty).TrimStart();
        if (!(trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))) {
            return false;
        }

        STJ.JsonNode? root;
        try {
            root = STJ.JsonNode.Parse(trimmed);
        } catch {
            return false;
        }
        if (root is null) {
            return false;
        }

        root = SanitizeNode(root, apiKey, propertyName: null);
        if (root is null) {
            return false;
        }
        try {
            sanitized = root.ToJsonString(new JsonSerializerOptions {
                WriteIndented = false
            });
            return !string.IsNullOrWhiteSpace(sanitized);
        } catch {
            return false;
        }
    }

    private static STJ.JsonNode? SanitizeNode(STJ.JsonNode? node, string? apiKey, string? propertyName) {
        if (node is null) {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(propertyName) && IsSensitiveKey(propertyName)) {
            return STJ.JsonValue.Create("[REDACTED]");
        }

        if (node is STJ.JsonObject obj) {
            foreach (var kvp in obj.ToList()) {
                obj[kvp.Key] = SanitizeNode(kvp.Value, apiKey, kvp.Key);
            }
            return obj;
        }
        if (node is STJ.JsonArray arr) {
            for (var i = 0; i < arr.Count; i++) {
                arr[i] = SanitizeNode(arr[i], apiKey, propertyName: null);
            }
            return arr;
        }
        if (node is STJ.JsonValue value) {
            if (value.TryGetValue<string>(out var s)) {
                var updated = s;
                if (!string.IsNullOrWhiteSpace(apiKey) && updated.Contains(apiKey.Trim(), StringComparison.Ordinal)) {
                    updated = updated.Replace(apiKey.Trim(), "[REDACTED]");
                }
                updated = BearerTokenRegex.Replace(updated, "Bearer [REDACTED]");
                if (!string.Equals(updated, s, StringComparison.Ordinal)) {
                    return STJ.JsonValue.Create(updated);
                }
            }
            return node;
        }
        return node;
    }

    private static bool IsSensitiveKey(string key) {
        if (string.IsNullOrWhiteSpace(key)) {
            return false;
        }
        var normalized = key.Trim().ToLowerInvariant();
        return normalized.Contains("api_key", StringComparison.Ordinal) ||
               normalized.Contains("apikey", StringComparison.Ordinal) ||
               normalized.Contains("api-key", StringComparison.Ordinal) ||
               normalized.Contains("token", StringComparison.Ordinal) ||
               normalized.Contains("secret", StringComparison.Ordinal) ||
               normalized.Contains("authorization", StringComparison.Ordinal);
    }
}






