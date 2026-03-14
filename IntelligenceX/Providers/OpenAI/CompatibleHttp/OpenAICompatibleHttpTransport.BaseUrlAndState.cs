using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.OpenAI.CompatibleHttp;

internal sealed partial class OpenAICompatibleHttpTransport {
    private static Uri NormalizeBaseUrl(string baseUrl) {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) || uri is null) {
            throw new ArgumentException("BaseUrl must be an absolute URI.", nameof(baseUrl));
        }

        // Normalize so common local-provider forms work:
        // - http://localhost:11434          -> http://localhost:11434/v1/
        // - http://localhost:1234/v1       -> http://localhost:1234/v1/
        // - https://example.com/openai/v1  -> https://example.com/openai/v1/
        var builder = new UriBuilder(uri);
        var path = builder.Path ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path) || path == "/") {
            builder.Path = "/v1/";
            return builder.Uri;
        }

        path = path.TrimEnd('/');
        if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) {
            builder.Path = path + "/";
            return builder.Uri;
        }

        var finalPath = builder.Path ?? string.Empty;
        if (!finalPath.EndsWith("/", StringComparison.Ordinal)) {
            builder.Path = finalPath + "/";
        }

        return builder.Uri;
    }

    private static Uri? BuildLmStudioModelsUrl(Uri apiBase) {
        if (!IsLikelyLmStudioEndpoint(apiBase)) {
            return null;
        }

        var builder = new UriBuilder(apiBase) {
            Path = "/api/v0/models",
            Query = string.Empty
        };
        return builder.Uri;
    }

    private static bool IsLikelyLmStudioEndpoint(Uri apiBase) {
        return OpenAICompatibleHttpProviderDetector.IsLikelyLmStudioEndpoint(apiBase);
    }

    private static Task<string> ReadAsStringAsync(HttpContent content, CancellationToken cancellationToken) {
#if NETSTANDARD2_0 || NET472
        cancellationToken.ThrowIfCancellationRequested();
        return content.ReadAsStringAsync();
#else
        return content.ReadAsStringAsync(cancellationToken);
#endif
    }

    private static Task<Stream> ReadAsStreamAsync(HttpContent content, CancellationToken cancellationToken) {
#if NETSTANDARD2_0 || NET472
        cancellationToken.ThrowIfCancellationRequested();
        return content.ReadAsStreamAsync();
#else
        return content.ReadAsStreamAsync(cancellationToken);
#endif
    }

    public void Dispose() {
        _http.Dispose();
    }

    private sealed class CompatibleThreadState {
        public CompatibleThreadState(string model) {
            Model = model;
        }

        public string Model { get; set; }
        public List<JsonObject> Messages { get; } = new();
        public string? Instructions { get; private set; }

        public void SetInstructions(string instructions) {
            if (string.IsNullOrWhiteSpace(instructions)) {
                return;
            }

            var normalized = instructions.Trim();
            if (string.Equals(Instructions, normalized, StringComparison.Ordinal)) {
                return;
            }

            Instructions = normalized;
            var sys = new JsonObject()
                .Add("role", "system")
                .Add("content", normalized);

            if (Messages.Count > 0 && string.Equals(Messages[0].GetString("role"), "system", StringComparison.OrdinalIgnoreCase)) {
                Messages[0] = sys;
            } else {
                Messages.Insert(0, sys);
            }
        }
    }

    private sealed class ToolCallBuilder {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new();
    }

    private sealed class ChatCompletionResponse {
        public ChatCompletionResponse(TurnInfo turn, JsonObject assistantMessageForHistory) {
            Turn = turn;
            AssistantMessageForHistory = assistantMessageForHistory;
        }

        public TurnInfo Turn { get; }
        public JsonObject AssistantMessageForHistory { get; }
    }
}
