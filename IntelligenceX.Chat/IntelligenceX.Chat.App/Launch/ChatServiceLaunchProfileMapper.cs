using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.App.Launch;

/// <summary>
/// Maps persisted desktop profile state to the local service launch contract.
/// </summary>
internal static class ChatServiceLaunchProfileMapper {
    private const string DefaultProfileName = "default";
    private const string NativeTransport = "native";
    private const string CompatibleHttpTransport = "compatible-http";
    private const string CopilotCliTransport = "copilot-cli";
    public const string DefaultOllamaBaseUrl = "http://127.0.0.1:11434";
    public const string DefaultLmStudioBaseUrl = "http://127.0.0.1:1234/v1";

    /// <summary>
    /// Normalizes the desktop profile name shared by every application shell.
    /// </summary>
    public static string NormalizeProfileName(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? DefaultProfileName : normalized;
    }

    /// <summary>
    /// Creates service launch options from the persisted desktop profile state.
    /// </summary>
    public static ChatServiceLaunchProfileOptions Create(
        ChatAppState state,
        IReadOnlyList<ChatServicePackToggle>? packToggles = null) {
        ArgumentNullException.ThrowIfNull(state);

        var profileName = NormalizeProfileName(state.ProfileName);
        var model = NormalizeOptional(state.LocalProviderModel);
        var transport = NormalizeTransport(state.LocalProviderTransport);
        var baseUrl = NormalizeBaseUrl(state.LocalProviderBaseUrl, transport, state.LocalProviderTransport);
        var authMode = NormalizeOptional(state.LocalProviderOpenAIAuthMode);
        var basicUsername = NormalizeOptional(state.LocalProviderOpenAIBasicUsername);
        var accountId = NormalizeOptional(state.LocalProviderOpenAIAccountId);
        var reasoningEffort = NormalizeOptional(state.LocalProviderReasoningEffort);
        var reasoningSummary = NormalizeOptional(state.LocalProviderReasoningSummary);
        var textVerbosity = NormalizeOptional(state.LocalProviderTextVerbosity);
        var imageQuality = NormalizeOptional(state.LocalProviderImageGenerationQuality);
        var imageSize = NormalizeOptional(state.LocalProviderImageGenerationSize);
        var imageOutputFormat = NormalizeOptional(state.LocalProviderImageGenerationOutputFormat);
        var imageBackground = NormalizeOptional(state.LocalProviderImageGenerationBackground);
        var imageOutputDirectory = NormalizeOptional(state.LocalProviderImageGenerationOutputDirectory);
        var hasImageGenerationOverrides = HasImageGenerationLaunchOverrides(state);

        return new ChatServiceLaunchProfileOptions {
            LoadProfileName = profileName,
            SaveProfileName = profileName,
            Model = model,
            OpenAITransport = transport,
            OpenAIBaseUrl = baseUrl,
            OpenAIAuthMode = authMode,
            OpenAIBasicUsername = basicUsername,
            OpenAIAccountId = accountId,
            OpenAIStreaming = true,
            OpenAIAllowInsecureHttp = ShouldAllowInsecureHttp(transport, baseUrl),
            ReasoningEffort = reasoningEffort,
            ReasoningSummary = reasoningSummary,
            TextVerbosity = textVerbosity,
            Temperature = state.LocalProviderTemperature,
            ImageGenerationEnabled = hasImageGenerationOverrides ? state.LocalProviderImageGenerationEnabled : null,
            ImageGenerationQuality = hasImageGenerationOverrides ? imageQuality : null,
            ClearImageGenerationQuality = hasImageGenerationOverrides && imageQuality is null,
            ImageGenerationSize = hasImageGenerationOverrides ? imageSize : null,
            ClearImageGenerationSize = hasImageGenerationOverrides && imageSize is null,
            ImageGenerationOutputFormat = hasImageGenerationOverrides ? imageOutputFormat : null,
            ClearImageGenerationOutputFormat = hasImageGenerationOverrides && imageOutputFormat is null,
            ImageGenerationOutputCompression = hasImageGenerationOverrides ? state.LocalProviderImageGenerationOutputCompression : null,
            ClearImageGenerationOutputCompression = hasImageGenerationOverrides && state.LocalProviderImageGenerationOutputCompression is null,
            ImageGenerationBackground = hasImageGenerationOverrides ? imageBackground : null,
            ClearImageGenerationBackground = hasImageGenerationOverrides && imageBackground is null,
            ImageGenerationOutputDirectory = hasImageGenerationOverrides ? imageOutputDirectory : null,
            ClearImageGenerationOutputDirectory = hasImageGenerationOverrides && imageOutputDirectory is null,
            PackToggles = packToggles
        };
    }

    /// <summary>
    /// Returns whether image-generation values should override the loaded service profile.
    /// </summary>
    public static bool HasImageGenerationLaunchOverrides(ChatAppState state) {
        ArgumentNullException.ThrowIfNull(state);
        return state.LocalProviderImageGenerationOverrideActive;
    }

    /// <summary>
    /// Resolves the persisted image-generation override switch, including profiles written before the switch existed.
    /// </summary>
    public static bool ResolveImageGenerationOverrideActive(ChatAppState state, bool isLoadedProfile) {
        ArgumentNullException.ThrowIfNull(state);
        return state.LocalProviderImageGenerationOverrideActive
               || isLoadedProfile && !state.LocalProviderImageGenerationOverrideActiveWasPresent;
    }

    /// <summary>
    /// Determines whether compatible HTTP needs the explicit insecure HTTP opt-in.
    /// </summary>
    public static bool ShouldAllowInsecureHttp(string? transport, string? baseUrl) {
        if (!string.Equals(NormalizeTransport(transport), CompatibleHttpTransport, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var value = (baseUrl ?? string.Empty).Trim();
        if (value.Length == 0) {
            return true;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes legacy transport aliases to the service protocol transport token.
    /// </summary>
    public static string NormalizeTransport(string? value) {
        _ = TryNormalizeTransport(value, out var transport);
        return transport;
    }

    /// <summary>
    /// Tries to normalize a recognized transport or returns the native fallback.
    /// </summary>
    public static bool TryNormalizeTransport(string? value, out string transport) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized) {
            case NativeTransport:
                transport = NativeTransport;
                return true;
            case "compatible-http":
            case "compatiblehttp":
            case "http":
            case "local":
            case "ollama":
            case "lmstudio":
            case "lm-studio":
                transport = CompatibleHttpTransport;
                return true;
            case "copilot":
            case "copilot-cli":
            case "github-copilot":
            case "githubcopilot":
                transport = CopilotCliTransport;
                return true;
            default:
                transport = NativeTransport;
                return false;
        }
    }

    /// <summary>
    /// Resolves the canonical compatible-HTTP endpoint, preserving the legacy LM Studio alias.
    /// </summary>
    public static string? NormalizeBaseUrl(string? value, string? transport, string? transportHint = null) {
        var normalizedTransport = NormalizeTransport(transport);
        if (!string.Equals(normalizedTransport, CompatibleHttpTransport, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length > 0) {
            return normalized.TrimEnd('/');
        }

        var hint = (transportHint ?? string.Empty).Trim().ToLowerInvariant();
        return hint is "lmstudio" or "lm-studio" ? DefaultLmStudioBaseUrl : DefaultOllamaBaseUrl;
    }

    /// <summary>
    /// Repairs persisted provider aliases before either desktop shell consumes the state.
    /// </summary>
    public static void NormalizeProviderState(ChatAppState state) {
        ArgumentNullException.ThrowIfNull(state);
        var transportHint = state.LocalProviderTransport;
        state.LocalProviderTransport = NormalizeTransport(transportHint);
        state.LocalProviderBaseUrl = NormalizeBaseUrl(state.LocalProviderBaseUrl, state.LocalProviderTransport, transportHint);

        var cacheTransportHint = state.CachedModelsTransport;
        state.CachedModelsTransport = NormalizeTransport(cacheTransportHint);
        state.CachedModelsBaseUrl = NormalizeBaseUrl(
            state.CachedModelsBaseUrl,
            state.CachedModelsTransport,
            cacheTransportHint);
    }

    private static string? NormalizeOptional(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}
