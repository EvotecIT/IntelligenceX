using System;

namespace IntelligenceX.OpenAI.CompatibleHttp;

internal static class OpenAICompatibleHttpProviderDetector {
    public static string? InferTelemetryProviderId(string? baseUrl) {
        if (!TryParseBaseUrl(baseUrl, out var uri)) {
            return null;
        }

        if (IsLikelyLmStudioEndpoint(uri)) {
            return "lmstudio";
        }

        if (IsLikelyOllamaEndpoint(uri)) {
            return "ollama";
        }

        if (IsLikelyChatGptEndpoint(uri)) {
            return "chatgpt";
        }

        return null;
    }

    public static bool IsLikelyLmStudioEndpoint(Uri apiBase) {
        if (apiBase is null) {
            return false;
        }

        if (apiBase.Port == 1234) {
            return true;
        }

        var host = apiBase.Host ?? string.Empty;
        return host.IndexOf("lmstudio", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsLikelyOllamaEndpoint(Uri apiBase) {
        if (apiBase is null) {
            return false;
        }

        if (apiBase.Port == 11434) {
            return true;
        }

        var host = apiBase.Host ?? string.Empty;
        return host.IndexOf("ollama", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsLikelyChatGptEndpoint(Uri apiBase) {
        if (apiBase is null) {
            return false;
        }

        var host = apiBase.Host ?? string.Empty;
        if (host.IndexOf("chatgpt.com", StringComparison.OrdinalIgnoreCase) >= 0) {
            return true;
        }

        var path = apiBase.AbsolutePath ?? string.Empty;
        return path.IndexOf("/backend-api", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool TryParseBaseUrl(string? baseUrl, out Uri uri) {
        uri = null!;
        var normalized = baseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) {
            return false;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var parsedUri) || parsedUri is null) {
            return false;
        }

        uri = parsedUri;
        return true;
    }
}
