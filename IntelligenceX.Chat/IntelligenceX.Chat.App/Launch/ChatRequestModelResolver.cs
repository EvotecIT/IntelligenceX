using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.App.Launch;

/// <summary>
/// Resolves the request model and persisted catalog scope for every desktop shell.
/// </summary>
internal static class ChatRequestModelResolver {
    private const string TransportNative = "native";
    private const string TransportCompatibleHttp = "compatible-http";
    private const string TransportCopilotCli = "copilot-cli";
    private const string DefaultOllamaBaseUrl = "http://127.0.0.1:11434";

    /// <summary>
    /// Resolves a configured model against the catalog exposed by the selected runtime.
    /// </summary>
    public static string? Resolve(
        string? transport,
        string? baseUrl,
        string? configuredModel,
        IReadOnlyList<ModelInfoDto>? availableModels) {
        var normalizedTransport = NormalizeTransport(transport);
        var normalizedConfiguredModel = (configuredModel ?? string.Empty).Trim();
        var localCompatibleRuntime = string.Equals(normalizedTransport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)
                                     && CompatibleProviderEndpointPolicy.IsLocalRuntimePreset(
                                         CompatibleProviderEndpointPolicy.DetectPreset(baseUrl));
        var supportsCatalogFallback =
            string.Equals(normalizedTransport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedTransport, TransportCopilotCli, StringComparison.OrdinalIgnoreCase);
        if (!supportsCatalogFallback) {
            return normalizedConfiguredModel.Length == 0 ? null : normalizedConfiguredModel;
        }

        var preferredModel = ResolvePreferredCatalogModel(availableModels);
        if (normalizedConfiguredModel.Length == 0) {
            return preferredModel.Length == 0 ? null : preferredModel;
        }

        if (CatalogContainsModel(availableModels, normalizedConfiguredModel)) {
            return normalizedConfiguredModel;
        }

        if (preferredModel.Length == 0) {
            if (localCompatibleRuntime && IsLikelyCloudHostedModelName(normalizedConfiguredModel)) {
                return null;
            }

            return normalizedConfiguredModel;
        }

        if (localCompatibleRuntime || IsLikelyCloudHostedModelName(normalizedConfiguredModel)) {
            return preferredModel;
        }

        return normalizedConfiguredModel;
    }

    /// <summary>
    /// Returns a cached catalog only when it belongs to the profile's current runtime endpoint.
    /// </summary>
    public static IReadOnlyList<ModelInfoDto>? ResolveCachedCatalog(ChatAppState state) {
        ArgumentNullException.ThrowIfNull(state);
        if (state.CachedModels is not { Count: > 0 }) {
            return null;
        }

        var runtimeTransport = NormalizeTransport(state.LocalProviderTransport);
        var cacheTransport = NormalizeTransport(state.CachedModelsTransport);
        if (!string.Equals(runtimeTransport, cacheTransport, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var runtimeBaseUrl = NormalizeBaseUrl(state.LocalProviderBaseUrl, runtimeTransport);
        var cacheBaseUrl = NormalizeBaseUrl(state.CachedModelsBaseUrl, cacheTransport);
        return string.Equals(runtimeBaseUrl, cacheBaseUrl, StringComparison.OrdinalIgnoreCase)
            ? state.CachedModels
            : null;
    }

    private static string NormalizeTransport(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "compatible-http" or "compatiblehttp" or "http" or "local" or "ollama" or "lmstudio" or "lm-studio" =>
                TransportCompatibleHttp,
            "copilot" or "copilot-cli" or "github-copilot" or "githubcopilot" => TransportCopilotCli,
            _ => TransportNative
        };
    }

    private static string NormalizeBaseUrl(string? value, string transport) {
        if (!string.Equals(transport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }

        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? DefaultOllamaBaseUrl : normalized.TrimEnd('/');
    }

    private static bool IsLikelyCloudHostedModelName(string? modelName) {
        var normalized = (modelName ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return false;
        }

        return normalized.StartsWith("gpt-", StringComparison.Ordinal)
               || string.Equals(normalized, "gpt5", StringComparison.Ordinal)
               || normalized.StartsWith("chatgpt", StringComparison.Ordinal)
               || normalized.StartsWith("o1", StringComparison.Ordinal)
               || normalized.StartsWith("o3", StringComparison.Ordinal)
               || normalized.StartsWith("o4", StringComparison.Ordinal);
    }

    private static bool CatalogContainsModel(IReadOnlyList<ModelInfoDto>? availableModels, string model) {
        if (availableModels is null || availableModels.Count == 0) {
            return false;
        }

        for (var i = 0; i < availableModels.Count; i++) {
            var candidate = (availableModels[i].Model ?? string.Empty).Trim();
            if (candidate.Length > 0 && string.Equals(candidate, model, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static string ResolvePreferredCatalogModel(IReadOnlyList<ModelInfoDto>? availableModels) {
        if (availableModels is null || availableModels.Count == 0) {
            return string.Empty;
        }

        var first = string.Empty;
        for (var i = 0; i < availableModels.Count; i++) {
            var entry = availableModels[i];
            var model = (entry.Model ?? string.Empty).Trim();
            if (model.Length == 0) {
                continue;
            }

            if (first.Length == 0) {
                first = model;
            }

            if (entry.IsDefault == true) {
                return model;
            }
        }

        return first;
    }
}
