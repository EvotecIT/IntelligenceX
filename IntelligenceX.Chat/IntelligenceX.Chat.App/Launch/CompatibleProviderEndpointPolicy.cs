using System;

namespace IntelligenceX.Chat.App.Launch;

/// <summary>
/// Classifies compatible-HTTP endpoints for every desktop shell.
/// </summary>
internal static class CompatibleProviderEndpointPolicy {
    public const string ManualPreset = "manual";
    public const string LmStudioPreset = "lmstudio";
    public const string OllamaPreset = "ollama";
    public const string OpenAiPreset = "openai";
    public const string AzureOpenAiPreset = "azure-openai";
    public const string AnthropicBridgePreset = "anthropic-bridge";
    public const string GeminiBridgePreset = "gemini-bridge";

    /// <summary>
    /// Detects a known compatible provider without treating a hostname substring as a loopback endpoint.
    /// </summary>
    public static string DetectPreset(string? baseUrl) {
        var normalized = (baseUrl ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return ManualPreset;
        }

        if (TryParseEndpoint(normalized, out var endpoint)) {
            var host = endpoint.Host;
            if (IsLoopbackHost(host) && endpoint.Port == 1234) {
                return LmStudioPreset;
            }

            if (IsLoopbackHost(host) && endpoint.Port == 11434) {
                return OllamaPreset;
            }

            if (string.Equals(host, "api.openai.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".api.openai.com", StringComparison.OrdinalIgnoreCase)) {
                return OpenAiPreset;
            }

            if (host.EndsWith(".openai.azure.com", StringComparison.OrdinalIgnoreCase)) {
                return AzureOpenAiPreset;
            }

            if (host.Contains("anthropic", StringComparison.OrdinalIgnoreCase)) {
                return AnthropicBridgePreset;
            }

            if (host.Contains("gemini", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".googleapis.com", StringComparison.OrdinalIgnoreCase)) {
                return GeminiBridgePreset;
            }
        }

        if (normalized.Contains("claude", StringComparison.OrdinalIgnoreCase)) {
            return AnthropicBridgePreset;
        }

        return ManualPreset;
    }

    /// <summary>
    /// Returns whether a preset represents a loopback model runtime.
    /// </summary>
    public static bool IsLocalRuntimePreset(string? preset) =>
        string.Equals(preset, LmStudioPreset, StringComparison.OrdinalIgnoreCase)
        || string.Equals(preset, OllamaPreset, StringComparison.OrdinalIgnoreCase);

    private static bool TryParseEndpoint(string value, out Uri endpoint) {
        if (Uri.TryCreate(value, UriKind.Absolute, out endpoint!)
            && !string.IsNullOrWhiteSpace(endpoint.Host)) {
            return true;
        }

        return Uri.TryCreate("http://" + value, UriKind.Absolute, out endpoint!)
               && !string.IsNullOrWhiteSpace(endpoint.Host);
    }

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
}
