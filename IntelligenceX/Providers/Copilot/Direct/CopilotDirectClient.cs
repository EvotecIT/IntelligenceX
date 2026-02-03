using System;
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
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private bool _disposed;

    /// <summary>
    /// Initializes a new direct Copilot client with the provided options.
    /// </summary>
    /// <param name="options">Direct client options.</param>
    public CopilotDirectClient(CopilotDirectOptions options) {
        options.Validate();
        _endpoint = new Uri(options.Url!);
        _http = new HttpClient {
            Timeout = options.Timeout
        };

        if (!string.IsNullOrWhiteSpace(options.Token)) {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        }
        foreach (var entry in options.Headers) {
            if (string.IsNullOrWhiteSpace(entry.Key)) {
                continue;
            }
            _http.DefaultRequestHeaders.Remove(entry.Key);
            _http.DefaultRequestHeaders.Add(entry.Key, entry.Value);
        }
    }

    /// <summary>
    /// Sends a chat request and returns the response text.
    /// </summary>
    /// <param name="prompt">Prompt text to send.</param>
    /// <param name="model">Model identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string> ChatAsync(string prompt, string model, CancellationToken cancellationToken = default) {
        var payload = new JsonObject()
            .Add("model", model)
            .Add("messages", new JsonArray()
                .Add(new JsonObject()
                    .Add("role", "user")
                    .Add("content", prompt)))
            .Add("stream", false);

        var json = JsonLite.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(_endpoint, content, cancellationToken).ConfigureAwait(false);
        var responseText = await ReadResponseAsync(response, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"Copilot direct request failed ({(int)response.StatusCode}): {responseText}");
        }
        var parsed = JsonLite.Parse(responseText).AsObject();
        var output = TryExtractText(parsed);
        if (string.IsNullOrWhiteSpace(output)) {
            throw new InvalidOperationException("Copilot direct response did not contain text output.");
        }
        return output!;
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
        _http.Dispose();
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
}
