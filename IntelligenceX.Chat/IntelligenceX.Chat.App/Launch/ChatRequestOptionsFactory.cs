using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.App.Launch;

/// <summary>
/// Captures resolved desktop settings needed to build one service chat request.
/// </summary>
internal sealed record DesktopChatRequestSettings {
    public string? Model { get; init; }
    public string? ReasoningEffort { get; init; }
    public string? ReasoningSummary { get; init; }
    public string? TextVerbosity { get; init; }
    public double? Temperature { get; init; }
    public bool? ImageGenerationEnabled { get; init; }
    public string? ImageGenerationQuality { get; init; }
    public string? ImageGenerationSize { get; init; }
    public string? ImageGenerationOutputFormat { get; init; }
    public int? ImageGenerationOutputCompression { get; init; }
    public string? ImageGenerationBackground { get; init; }
    public string? ImageGenerationOutputDirectory { get; init; }
    public IReadOnlyList<string>? DisabledTools { get; init; }
    public IReadOnlyList<string>? DisabledPackIds { get; init; }
    public int? MaxToolRounds { get; init; }
    public int? ServiceMaxToolRounds { get; init; }
    public bool? ParallelTools { get; init; }
    public bool ServiceParallelTools { get; init; } = true;
    public int? TurnTimeoutSeconds { get; init; }
    public int? ServiceTurnTimeoutSeconds { get; init; }
    public int? ToolTimeoutSeconds { get; init; }
    public int? ServiceToolTimeoutSeconds { get; init; }
    public bool? WeightedToolRouting { get; init; }
    public int? MaxCandidateTools { get; init; }
    public bool? PlanExecuteReviewLoop { get; init; }
    public int? MaxReviewPasses { get; init; }
    public int? ModelHeartbeatSeconds { get; init; }
    public bool IsLocalCompatibleRuntime { get; init; }
}

/// <summary>
/// Owns the per-turn request policy shared by native and legacy desktop shells.
/// </summary>
internal static class ChatRequestOptionsFactory {
    public const string ParallelToolModeAuto = "auto";
    public const string ParallelToolModeForceSerial = "force_serial";
    public const string ParallelToolModeAllowParallel = "allow_parallel";

    private const int LocalCompatibleMaxToolRounds = 8;
    private const int DefaultTurnTimeoutSeconds = 180;
    private const int DefaultToolTimeoutSeconds = 60;

    /// <summary>
    /// Creates a normalized request contract from resolved desktop state.
    /// </summary>
    public static ChatRequestOptions Create(DesktopChatRequestSettings settings) {
        ArgumentNullException.ThrowIfNull(settings);

        var parallelToolMode = ResolveParallelToolMode(settings.ParallelTools);
        return new ChatRequestOptions {
            Model = NormalizeOptional(settings.Model),
            ReasoningEffort = settings.ReasoningEffort,
            ReasoningSummary = settings.ReasoningSummary,
            TextVerbosity = settings.TextVerbosity,
            Temperature = settings.Temperature,
            ImageGenerationEnabled = settings.ImageGenerationEnabled,
            ImageGenerationQuality = settings.ImageGenerationQuality,
            ImageGenerationSize = settings.ImageGenerationSize,
            ImageGenerationOutputFormat = settings.ImageGenerationOutputFormat,
            ImageGenerationOutputCompression = settings.ImageGenerationOutputCompression,
            ImageGenerationBackground = settings.ImageGenerationBackground,
            ImageGenerationOutputDirectory = settings.ImageGenerationOutputDirectory,
            DisabledTools = NormalizeValues(settings.DisabledTools),
            DisabledPackIds = NormalizeValues(settings.DisabledPackIds),
            MaxToolRounds = NormalizeInt(
                                settings.MaxToolRounds,
                                ChatRequestOptionLimits.MinToolRounds,
                                ChatRequestOptionLimits.MaxToolRounds)
                            ?? NormalizeInt(
                                settings.ServiceMaxToolRounds,
                                ChatRequestOptionLimits.MinToolRounds,
                                ChatRequestOptionLimits.MaxToolRounds)
                            ?? (settings.IsLocalCompatibleRuntime
                                ? LocalCompatibleMaxToolRounds
                                : ChatRequestOptionLimits.DefaultToolRounds),
            ParallelTools = ResolveParallelTools(parallelToolMode, settings.ServiceParallelTools),
            ParallelToolMode = parallelToolMode,
            TurnTimeoutSeconds = NormalizeInt(
                                     settings.TurnTimeoutSeconds,
                                     ChatRequestOptionLimits.MinTimeoutSeconds,
                                     ChatRequestOptionLimits.MaxTimeoutSeconds)
                                 ?? NormalizePositiveTimeout(settings.ServiceTurnTimeoutSeconds)
                                 ?? DefaultTurnTimeoutSeconds,
            ToolTimeoutSeconds = NormalizeInt(
                                     settings.ToolTimeoutSeconds,
                                     ChatRequestOptionLimits.MinTimeoutSeconds,
                                     ChatRequestOptionLimits.MaxTimeoutSeconds)
                                 ?? NormalizePositiveTimeout(settings.ServiceToolTimeoutSeconds)
                                 ?? DefaultToolTimeoutSeconds,
            WeightedToolRouting = settings.WeightedToolRouting
                                  ?? (settings.IsLocalCompatibleRuntime ? false : null),
            MaxCandidateTools = NormalizeInt(
                settings.MaxCandidateTools,
                ChatRequestOptionLimits.MinCandidateTools,
                ChatRequestOptionLimits.MaxCandidateTools),
            PlanExecuteReviewLoop = settings.PlanExecuteReviewLoop ?? false,
            MaxReviewPasses = NormalizeInt(
                                  settings.MaxReviewPasses,
                                  0,
                                  ChatRequestOptionLimits.MaxReviewPasses)
                              ?? 0,
            ModelHeartbeatSeconds = NormalizeInt(
                                        settings.ModelHeartbeatSeconds,
                                        0,
                                        ChatRequestOptionLimits.MaxModelHeartbeatSeconds)
                                    ?? (settings.IsLocalCompatibleRuntime ? 0 : null)
        };
    }

    /// <summary>
    /// Creates request settings directly from persisted desktop profile state.
    /// </summary>
    public static ChatRequestOptions CreateFromState(
        ChatAppState state,
        string? conversationModelOverride = null,
        SessionPolicyDto? servicePolicy = null) {
        ArgumentNullException.ThrowIfNull(state);
        var imageOverridesActive = state.LocalProviderImageGenerationOverrideActive;
        var runtimeOverridesActive = state.LocalProviderRuntimeOverrideActive;
        var explicitConversationModel = NormalizeOptional(conversationModelOverride);
        var resolvedModel = explicitConversationModel;
        if (resolvedModel is null && runtimeOverridesActive) {
            resolvedModel = ChatRequestModelResolver.Resolve(
                state.LocalProviderTransport,
                state.LocalProviderBaseUrl,
                state.LocalProviderModel,
                ChatRequestModelResolver.ResolveCachedCatalog(state));
        }

        return Create(new DesktopChatRequestSettings {
            Model = resolvedModel,
            ReasoningEffort = runtimeOverridesActive ? state.LocalProviderReasoningEffort : null,
            ReasoningSummary = runtimeOverridesActive ? state.LocalProviderReasoningSummary : null,
            TextVerbosity = runtimeOverridesActive ? state.LocalProviderTextVerbosity : null,
            Temperature = runtimeOverridesActive ? state.LocalProviderTemperature : null,
            ImageGenerationEnabled = imageOverridesActive ? state.LocalProviderImageGenerationEnabled : null,
            ImageGenerationQuality = imageOverridesActive ? state.LocalProviderImageGenerationQuality : null,
            ImageGenerationSize = imageOverridesActive ? state.LocalProviderImageGenerationSize : null,
            ImageGenerationOutputFormat = imageOverridesActive ? state.LocalProviderImageGenerationOutputFormat : null,
            ImageGenerationOutputCompression = imageOverridesActive ? state.LocalProviderImageGenerationOutputCompression : null,
            ImageGenerationBackground = imageOverridesActive ? state.LocalProviderImageGenerationBackground : null,
            ImageGenerationOutputDirectory = imageOverridesActive ? state.LocalProviderImageGenerationOutputDirectory : null,
            DisabledTools = state.DisabledTools,
            MaxToolRounds = state.AutonomyMaxToolRounds,
            ServiceMaxToolRounds = servicePolicy?.MaxToolRounds,
            ParallelTools = state.AutonomyParallelTools,
            ServiceParallelTools = servicePolicy?.ParallelTools ?? true,
            TurnTimeoutSeconds = state.AutonomyTurnTimeoutSeconds,
            ServiceTurnTimeoutSeconds = servicePolicy?.TurnTimeoutSeconds,
            ToolTimeoutSeconds = state.AutonomyToolTimeoutSeconds,
            ServiceToolTimeoutSeconds = servicePolicy?.ToolTimeoutSeconds,
            WeightedToolRouting = state.AutonomyWeightedToolRouting,
            MaxCandidateTools = state.AutonomyMaxCandidateTools,
            PlanExecuteReviewLoop = state.AutonomyPlanExecuteReviewLoop,
            MaxReviewPasses = state.AutonomyMaxReviewPasses,
            ModelHeartbeatSeconds = state.AutonomyModelHeartbeatSeconds,
            IsLocalCompatibleRuntime = IsLocalCompatibleRuntime(
                state.LocalProviderTransport,
                state.LocalProviderBaseUrl)
        });
    }

    /// <summary>
    /// Returns whether the profile targets a local compatible-HTTP model server.
    /// </summary>
    public static bool IsLocalCompatibleRuntime(string? transport, string? baseUrl) {
        if (!string.Equals((transport ?? string.Empty).Trim(), "compatible-http", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return CompatibleProviderEndpointPolicy.IsLocalRuntimePreset(
            CompatibleProviderEndpointPolicy.DetectPreset(baseUrl));
    }

    public static string ResolveParallelToolMode(bool? parallelTools) => parallelTools switch {
        true => ParallelToolModeAllowParallel,
        false => ParallelToolModeForceSerial,
        _ => ParallelToolModeAuto
    };

    public static bool ResolveParallelTools(string parallelToolMode, bool serviceDefaultParallelTools) =>
        parallelToolMode switch {
            ParallelToolModeAllowParallel => true,
            ParallelToolModeForceSerial => false,
            _ => serviceDefaultParallelTools
        };

    private static string? NormalizeOptional(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string[]? NormalizeValues(IReadOnlyList<string>? values) {
        if (values is null || values.Count == 0) {
            return null;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<string>(values.Count);
        for (var i = 0; i < values.Count; i++) {
            var value = (values[i] ?? string.Empty).Trim();
            if (value.Length > 0 && seen.Add(value)) {
                normalized.Add(value);
            }
        }

        normalized.Sort(StringComparer.OrdinalIgnoreCase);
        return normalized.Count == 0 ? null : normalized.ToArray();
    }

    private static int? NormalizeInt(int? value, int min, int max) =>
        value.HasValue && value.Value >= min && value.Value <= max ? value : null;

    private static int? NormalizePositiveTimeout(int? value) =>
        value is > 0 and <= ChatRequestOptionLimits.MaxTimeoutSeconds ? value : null;
}
