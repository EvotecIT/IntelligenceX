using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.App.Launch;

/// <summary>
/// Maps persisted desktop profile state to the local service launch contract.
/// </summary>
internal static class ChatServiceLaunchProfileMapper {
    private const string DefaultProfileName = "default";
    private const string CompatibleHttpTransport = "compatible-http";

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
        var transport = NormalizeOptional(state.LocalProviderTransport);
        var baseUrl = NormalizeOptional(state.LocalProviderBaseUrl);
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
        return state.LocalProviderImageGenerationOverrideActive
               || state.LocalProviderImageGenerationEnabled
               || !string.IsNullOrWhiteSpace(state.LocalProviderImageGenerationQuality)
               || !string.IsNullOrWhiteSpace(state.LocalProviderImageGenerationSize)
               || !string.IsNullOrWhiteSpace(state.LocalProviderImageGenerationOutputFormat)
               || state.LocalProviderImageGenerationOutputCompression.HasValue
               || !string.IsNullOrWhiteSpace(state.LocalProviderImageGenerationBackground)
               || !string.IsNullOrWhiteSpace(state.LocalProviderImageGenerationOutputDirectory);
    }

    /// <summary>
    /// Determines whether compatible HTTP needs the explicit insecure HTTP opt-in.
    /// </summary>
    public static bool ShouldAllowInsecureHttp(string? transport, string? baseUrl) {
        if (!string.Equals(transport, CompatibleHttpTransport, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var value = (baseUrl ?? string.Empty).Trim();
        if (value.Length == 0) {
            return true;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOptional(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}
