using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.Copilot.Direct;

/// <summary>
/// Experimental direct HTTP client for Copilot-compatible endpoints.
/// </summary>
/// <remarks>
/// This transport is unsupported and may change or be removed.
/// </remarks>
public sealed class CopilotDirectClient : IDisposable {
    private static readonly HttpClient SharedHttpClient = new() {
        Timeout = Timeout.InfiniteTimeSpan
    };
    private readonly Uri _endpoint;
    private readonly TimeSpan _timeout;
    private readonly AuthenticationHeaderValue? _authorization;
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _hasAuthorizationHeader;
    private bool _disposed;

    /// <summary>
    /// Initializes a new direct Copilot client with the provided options.
    /// </summary>
    /// <param name="options">Direct client options.</param>
    public CopilotDirectClient(CopilotDirectOptions options) {
        options.Validate();
        _endpoint = new Uri(options.Url!);
        _timeout = options.Timeout;

        if (!string.IsNullOrWhiteSpace(options.Token)) {
            _authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        }
        foreach (var entry in options.Headers) {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value is null) {
                continue;
            }
            if (ContainsNewline(entry.Key) || ContainsNewline(entry.Value)) {
                throw new ArgumentException("Copilot direct headers cannot contain newlines.", nameof(options));
            }
            _headers[entry.Key] = entry.Value;
            if (string.Equals(entry.Key, "Authorization", StringComparison.OrdinalIgnoreCase)) {
                _hasAuthorizationHeader = true;
            }
        }
    }

    /// <summary>
    /// Sends a chat request and returns the response text.
    /// </summary>
    /// <param name="prompt">Prompt text to send.</param>
    /// <param name="model">Model identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string> ChatAsync(string prompt, string model, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(prompt)) {
            throw new ArgumentException("Prompt cannot be empty.", nameof(prompt));
        }
        if (string.IsNullOrWhiteSpace(model)) {
            throw new ArgumentException("Model cannot be empty.", nameof(model));
        }

        var payload = new JsonObject()
            .Add("model", model)
            .Add("messages", new JsonArray()
                .Add(new JsonObject()
                    .Add("role", "user")
                    .Add("content", prompt)))
            .Add("stream", false);

        var json = JsonLite.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = content };
        if (!_hasAuthorizationHeader && _authorization is not null) {
            request.Headers.Authorization = _authorization;
        }
        foreach (var entry in _headers) {
            request.Headers.TryAddWithoutValidation(entry.Key, entry.Value);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_timeout > TimeSpan.Zero) {
            timeoutCts.CancelAfter(_timeout);
        }

        HttpResponseMessage response;
        try {
            response = await SharedHttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);
        } catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
            throw new TimeoutException("Copilot direct request timed out.", ex);
        } catch (HttpRequestException ex) {
            throw new InvalidOperationException("Copilot direct request failed. Check network connectivity.", ex);
        }

        using (response) {
            string responseText;
            try {
                responseText = await ReadResponseAsync(response, timeoutCts.Token).ConfigureAwait(false);
            } catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
                throw new TimeoutException("Copilot direct response read timed out.", ex);
            }
            if (!response.IsSuccessStatusCode) {
                var status = (int)response.StatusCode;
                throw new InvalidOperationException($"Copilot direct request failed (HTTP {status}).");
            }
            JsonObject? parsed;
            try {
                parsed = JsonLite.Parse(responseText).AsObject();
            } catch (Exception ex) {
                throw new InvalidOperationException("Copilot direct response was not valid JSON.", ex);
            }
            var output = TryExtractText(parsed);
            if (string.IsNullOrWhiteSpace(output)) {
                throw new InvalidOperationException("Copilot direct response did not contain text output.");
            }
            return output!;
        }
    }

    private static string? TryExtractText(JsonObject? obj) {
        if (obj is null) {
            return null;
        }
        var choices = obj.GetArray("choices");
        if (choices is not null && choices.Count > 0) {
            var first = choices[0].AsObject();
            var message = first?.GetObject("message");
            var content = message?.GetString("content");
            if (!string.IsNullOrWhiteSpace(content)) {
                return content;
            }
            var text = first?.GetString("text");
            if (!string.IsNullOrWhiteSpace(text)) {
                return text;
            }
        }
        var outputText = obj.GetString("output_text");
        if (!string.IsNullOrWhiteSpace(outputText)) {
            return outputText;
        }
        var output = obj.GetArray("output");
        if (output is not null && output.Count > 0) {
            var item = output[0].AsObject();
            var contentItems = item?.GetArray("content");
            if (contentItems is not null && contentItems.Count > 0) {
                var contentObj = contentItems[0].AsObject();
                var text = contentObj?.GetString("text");
                if (!string.IsNullOrWhiteSpace(text)) {
                    return text;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Disposes the client and its underlying HTTP resources.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
    }

    private static async Task<string> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
#if NET5_0_OR_GREATER
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        cancellationToken.ThrowIfCancellationRequested();
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return text;
#endif
    }

    private static bool ContainsNewline(string value) {
        return value.IndexOfAny(new[] { '\r', '\n' }) >= 0;
    }
}
