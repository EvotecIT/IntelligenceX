using System;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewRunner {
    private static readonly HttpClient ClaudeHttp = CreateClaudeHttp();
    private static readonly Regex ClaudeApiKeyRegex =
        new("[A-Za-z0-9_\\-]{16,}", RegexOptions.Compiled);

    private sealed record ClaudeResponseEnvelope(
        string? Id,
        string? Model,
        string Text,
        long? InputTokens,
        long? OutputTokens,
        string? OrganizationId);

    private static HttpClient CreateClaudeHttp() {
        return new HttpClient {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private async Task RunClaudePreflightAsync(TimeSpan timeout, CancellationToken cancellationToken) {
        var baseUri = ResolveClaudeBaseUri(_settings.AnthropicBaseUrl);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "/"));

        try {
            using var _ = await PreflightHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);
        } catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
            throw new TimeoutException(
                $"Claude health check timed out after {timeout.TotalSeconds:0.#}s for {baseUri.Host}.",
                ex);
        } catch (HttpRequestException ex) when (!ex.StatusCode.HasValue) {
            throw new InvalidOperationException(
                $"Claude health check failed for {baseUri.Host}. Check URL, DNS, proxy, and network settings.",
                ex);
        }
    }

    private async Task<string> RunClaudeWithRetryAsync(string prompt, CancellationToken cancellationToken) {
        var retryState = new ReviewRetryState();
        try {
            if (_settings.Preflight && !_settings.ProviderHealthChecks) {
                var timeout = _settings.PreflightTimeoutSeconds > 0
                    ? TimeSpan.FromSeconds(_settings.PreflightTimeoutSeconds)
                    : TimeSpan.FromSeconds(15);
                await RunClaudePreflightAsync(timeout, cancellationToken).ConfigureAwait(false);
            }

            return await ReviewRetryPolicy.RunAsync(
                    () => RunClaudeOnceAsync(prompt, cancellationToken),
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
                    retryState: retryState,
                    operationName: "Claude").ConfigureAwait(false);
        } catch (Exception ex) {
            ReviewDiagnostics.LogFailure(ex, _settings, snapshot: null, retryState);
            if (ShouldFailOpen(_settings, ex)) {
                return ReviewDiagnostics.BuildFailureBody(ex, _settings, snapshot: null, retryState);
            }
            throw;
        }
    }

    private async Task<string> RunClaudeOnceAsync(string prompt, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(_settings.Model)) {
            throw new InvalidOperationException("Claude provider requires review.model to be set.");
        }

        var endpoint = ResolveClaudeMessagesEndpoint(_settings.AnthropicBaseUrl);
        var apiKey = ResolveClaudeApiKey();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.AnthropicTimeoutSeconds)));

        var payload = new {
            model = _settings.Model,
            max_tokens = Math.Max(1, _settings.AnthropicMaxTokens),
            messages = new[] {
                new { role = "user", content = prompt }
            }
        };

        var requestJson = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", ResolveClaudeVersion(_settings.AnthropicVersion));

        var startedAt = DateTimeOffset.UtcNow;
        using var response = await ClaudeHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
            .ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) {
            var code = (int)response.StatusCode;
            var body = FormatClaudeErrorBody(responseText, apiKey, _settings.Diagnostics);
            throw new InvalidOperationException($"Claude request failed (HTTP {code}). {body}");
        }

        var parsed = ParseClaudeResponse(responseText, response);
        var completedAt = DateTimeOffset.UtcNow;
        ReviewerUsageTelemetryRecorder.TryRecordClaudeTurn(
            parsed.OrganizationId,
            parsed.OrganizationId,
            parsed.Model,
            parsed.Id,
            parsed.InputTokens,
            parsed.OutputTokens,
            completedAt,
            completedAt - startedAt);
        return parsed.Text;
    }

    private string ResolveClaudeApiKey() {
        var configuredEnvName = NormalizeOptional(_settings.AnthropicApiKeyEnv);
        if (!string.IsNullOrWhiteSpace(configuredEnvName)) {
            var envValue = NormalizeOptional(Environment.GetEnvironmentVariable(configuredEnvName));
            if (!string.IsNullOrWhiteSpace(envValue)) {
                SecretsAudit.Record($"Anthropic API key from {configuredEnvName}");
                return envValue!;
            }

            throw new InvalidOperationException(
                $"Claude provider requires an API key. review.anthropic.apiKeyEnv is set to \"{configuredEnvName}\", but {configuredEnvName} is empty. Set {configuredEnvName}, review.anthropic.apiKey, or ANTHROPIC_API_KEY.");
        }

        var standardValue = NormalizeOptional(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
        if (!string.IsNullOrWhiteSpace(standardValue)) {
            SecretsAudit.Record("Anthropic API key from ANTHROPIC_API_KEY");
            return standardValue!;
        }

        var configuredValue = NormalizeOptional(_settings.AnthropicApiKey);
        if (!string.IsNullOrWhiteSpace(configuredValue)) {
            SecretsAudit.Record("Anthropic API key from config (anthropic.apiKey)");
            return configuredValue!;
        }

        throw new InvalidOperationException(
            "Claude provider requires an API key. Set review.anthropic.apiKeyEnv, review.anthropic.apiKey, or ANTHROPIC_API_KEY.");
    }

    private static ClaudeResponseEnvelope ParseClaudeResponse(string responseText, HttpResponseMessage response) {
        if (string.IsNullOrWhiteSpace(responseText)) {
            throw new InvalidOperationException("Claude response was empty.");
        }

        try {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            var id = ReadClaudeString(root, "id");
            var model = ReadClaudeString(root, "model");
            var text = ExtractClaudeText(root);
            var inputTokens = ReadClaudeUsageLong(root, "input_tokens");
            var outputTokens = ReadClaudeUsageLong(root, "output_tokens");
            var organizationId = response.Headers.TryGetValues("anthropic-organization-id", out var values)
                ? NormalizeOptional(string.Join(",", values))
                : null;

            return new ClaudeResponseEnvelope(
                id,
                model,
                text,
                inputTokens,
                outputTokens,
                organizationId);
        } catch (JsonException ex) {
            throw new InvalidOperationException("Claude response was not valid JSON.", ex);
        }
    }

    private static string ExtractClaudeText(JsonElement root) {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != System.Text.Json.JsonValueKind.Array) {
            throw new InvalidOperationException("Claude response JSON missing content array.");
        }

        var builder = new StringBuilder();
        foreach (var item in content.EnumerateArray()) {
            if (item.ValueKind != System.Text.Json.JsonValueKind.Object) {
                continue;
            }

            var type = ReadClaudeString(item, "type");
            if (!string.Equals(type, "text", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var text = ReadClaudeString(item, "text");
            if (string.IsNullOrWhiteSpace(text)) {
                continue;
            }

            if (builder.Length > 0) {
                builder.AppendLine();
            }
            builder.Append(text);
        }

        var combined = builder.ToString().Trim();
        if (combined.Length == 0) {
            throw new InvalidOperationException("Claude response JSON did not contain any text content blocks.");
        }

        return combined;
    }

    private static long? ReadClaudeUsageLong(JsonElement root, string propertyName) {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != System.Text.Json.JsonValueKind.Object) {
            return null;
        }
        if (!usage.TryGetProperty(propertyName, out var value)) {
            return null;
        }

        return value.ValueKind switch {
            System.Text.Json.JsonValueKind.Number when value.TryGetInt64(out var number) => Math.Max(0L, number),
            _ => null
        };
    }

    private static string? ReadClaudeString(JsonElement obj, string propertyName) {
        if (!obj.TryGetProperty(propertyName, out var value) || value.ValueKind != System.Text.Json.JsonValueKind.String) {
            return null;
        }

        return NormalizeOptional(value.GetString());
    }

    private static Uri ResolveClaudeBaseUri(string? baseUrl) {
        var trimmed = NormalizeOptional(baseUrl) ?? "https://api.anthropic.com";
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var baseUri) || baseUri is null) {
            throw new InvalidOperationException($"Claude baseUrl is invalid: '{trimmed}'.");
        }

        if (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"Claude baseUrl must use http:// or https:// (got '{baseUri.Scheme}').");
        }

        return baseUri;
    }

    private static Uri ResolveClaudeMessagesEndpoint(string? baseUrl) {
        var baseUri = ResolveClaudeBaseUri(baseUrl);
        var normalized = baseUri.ToString().TrimEnd('/');
        if (normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) {
            normalized += "/messages";
        } else {
            normalized += "/v1/messages";
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var endpoint) || endpoint is null) {
            throw new InvalidOperationException($"Claude messages endpoint is invalid: '{normalized}'.");
        }

        return endpoint;
    }

    private static string ResolveClaudeVersion(string? value) {
        return NormalizeOptional(value) ?? "2023-06-01";
    }

    private static string FormatClaudeErrorBody(string? content, string apiKey, bool diagnostics) {
        if (!diagnostics) {
            return "Response body omitted (set review.diagnostics=true to include sanitized output).";
        }

        if (string.IsNullOrWhiteSpace(content)) {
            return "Response body was empty.";
        }

        var sanitized = content.Trim();
        sanitized = sanitized.Replace(apiKey, "[REDACTED]");
        sanitized = ClaudeApiKeyRegex.Replace(sanitized, match =>
            string.Equals(match.Value, apiKey, StringComparison.Ordinal) ? "[REDACTED]" : match.Value);
        return sanitized;
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    internal string ResolveClaudeApiKeyForTests() {
        return ResolveClaudeApiKey();
    }
}
