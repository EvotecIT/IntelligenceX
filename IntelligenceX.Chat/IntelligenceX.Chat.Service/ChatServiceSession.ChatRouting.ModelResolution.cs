using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private async Task<string> ResolveTurnModelAsync(IntelligenceXClient client, ChatRequest request, CancellationToken cancellationToken) {
        var requestedModel = (request.Options?.Model ?? string.Empty).Trim();
        if (requestedModel.Length > 0) {
            return requestedModel;
        }

        var fallback = (_options.Model ?? string.Empty).Trim();
        if (_options.OpenAITransport != OpenAITransportKind.CompatibleHttp || !IsLoopbackEndpoint(_options.OpenAIBaseUrl)) {
            return fallback.Length > 0 ? fallback : DefaultRuntimeModel;
        }

        var shouldProbeLocalCatalog = ShouldAutoResolveLocalCompatibleModel(fallback);
        if (!shouldProbeLocalCatalog) {
            return fallback;
        }

        var autoSelected = await TryResolveLocalCompatibleModelAsync(
                client,
                cancellationToken,
                allowNetworkFetch: true)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(autoSelected)) {
            return autoSelected!;
        }

        return fallback.Length > 0 ? fallback : DefaultRuntimeModel;
    }

    private static bool ShouldAutoResolveLocalCompatibleModel(string fallbackModel) {
        if (string.IsNullOrWhiteSpace(fallbackModel)) {
            return true;
        }

        return string.Equals(fallbackModel, DefaultRuntimeModel, StringComparison.OrdinalIgnoreCase);
    }

    internal static string? SelectLocalCompatibleModel(IReadOnlyList<ModelInfo>? models) {
        if (models is null || models.Count == 0) {
            return null;
        }

        string? firstModel = null;
        string? defaultModel = null;
        string? loadedModel = null;
        string? loadedDefaultModel = null;

        for (var i = 0; i < models.Count; i++) {
            var entry = models[i];
            if (entry is null) {
                continue;
            }

            var model = (entry.Model ?? string.Empty).Trim();
            if (model.Length == 0) {
                continue;
            }

            firstModel ??= model;

            var isDefault = entry.IsDefault;
            var runtimeState = (entry.RuntimeState ?? string.Empty).Trim();
            var isLoaded = runtimeState.Equals("loaded", StringComparison.OrdinalIgnoreCase)
                           || runtimeState.Equals("ready", StringComparison.OrdinalIgnoreCase)
                           || runtimeState.Equals("running", StringComparison.OrdinalIgnoreCase);

            if (isDefault && defaultModel is null) {
                defaultModel = model;
            }

            if (isLoaded && loadedModel is null) {
                loadedModel = model;
            }

            if (isLoaded && isDefault && loadedDefaultModel is null) {
                loadedDefaultModel = model;
            }
        }

        return loadedDefaultModel ?? loadedModel ?? defaultModel ?? firstModel;
    }

    private async Task<string?> TryResolveLocalCompatibleModelAsync(
        IntelligenceXClient client,
        CancellationToken cancellationToken,
        bool allowNetworkFetch) {
        var cachedModel = TryResolveLocalCompatibleModelFromCache();
        if (!string.IsNullOrWhiteSpace(cachedModel)) {
            return cachedModel;
        }

        if (!allowNetworkFetch) {
            return null;
        }

        try {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
            var result = await client.ListModelsAsync(timeoutCts.Token).ConfigureAwait(false);
            lock (_modelListCacheLock) {
                _modelListCache = new ModelListCacheEntry(
                    Key: BuildModelListCacheKeyForCurrentRuntime(),
                    ExpiresAtUtc: DateTime.UtcNow.AddMinutes(5),
                    Result: result);
            }

            return SelectLocalCompatibleModel(result.Models);
        } catch {
            return null;
        }
    }

    private string? TryResolveLocalCompatibleModelFromCache() {
        ModelListResult? modelList = null;
        var cacheKey = BuildModelListCacheKeyForCurrentRuntime();
        lock (_modelListCacheLock) {
            if (_modelListCache is not null
                && string.Equals(_modelListCache.Key, cacheKey, StringComparison.Ordinal)) {
                modelList = _modelListCache.Result;
            }
        }

        return modelList is null ? null : SelectLocalCompatibleModel(modelList.Models);
    }

    private string BuildModelListCacheKeyForCurrentRuntime() {
        var profileName = (_options.ProfileName ?? string.Empty).Trim();
        return $"{profileName}|{_options.OpenAITransport}|{_options.OpenAIBaseUrl ?? string.Empty}";
    }

    internal static bool IsLoopbackEndpoint(string? baseUrl) {
        var trimmed = (baseUrl ?? string.Empty).Trim();
        if (trimmed.Length == 0) {
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) {
            return false;
        }

        return uri.IsLoopback;
    }

    internal static string BuildNoTextResponseFallbackText(string? model, OpenAITransportKind transport, string? baseUrl) {
        var normalizedModel = (model ?? string.Empty).Trim();
        if (normalizedModel.Length == 0) {
            normalizedModel = "unknown";
        }

        if (transport == OpenAITransportKind.CompatibleHttp) {
            var endpoint = string.IsNullOrWhiteSpace(baseUrl) ? "configured endpoint" : baseUrl!.Trim();
            return "[warning] No response text was produced by the runtime.\n\n"
                   + "Model: " + normalizedModel + "\n"
                   + "Endpoint: " + endpoint + "\n\n"
                   + "Try a different model, then run Refresh Models and retry.";
        }

        return "[warning] No response text was produced by the model.\n\n"
               + "Model: " + normalizedModel + "\n\n"
               + "Retry the turn, or choose a different model.";
    }

    private string? BuildTurnInstructionsWithRuntimeIdentity(string resolvedModel) {
        var baseInstructions = (_instructions ?? string.Empty).Trim();
        var model = (resolvedModel ?? string.Empty).Trim();
        if (model.Length == 0) {
            return baseInstructions.Length == 0 ? null : baseInstructions;
        }

        var runtimeIdentity = new StringBuilder();
        runtimeIdentity.AppendLine("[Runtime identity]");
        runtimeIdentity.AppendLine("ix:runtime-identity:v1");
        runtimeIdentity.AppendLine("active model: " + model);
        runtimeIdentity.AppendLine("transport: " + _options.OpenAITransport.ToString().ToLowerInvariant());
        if (_options.OpenAITransport == OpenAITransportKind.CompatibleHttp && !string.IsNullOrWhiteSpace(_options.OpenAIBaseUrl)) {
            runtimeIdentity.AppendLine("endpoint: " + _options.OpenAIBaseUrl!.Trim());
        }
        runtimeIdentity.AppendLine("When asked about the current model or runtime, use the exact active model value above. Do not guess.");

        var runtimeIdentityText = runtimeIdentity.ToString().Trim();
        if (baseInstructions.Length == 0) {
            return runtimeIdentityText;
        }

        return baseInstructions + "\n\n" + runtimeIdentityText;
    }

    private static string BuildCompatibleRuntimeNoTextDirectRetryPrompt(string userRequest) {
        var normalizedRequest = (userRequest ?? string.Empty).Trim();
        if (normalizedRequest.Length == 0) {
            normalizedRequest = "Please provide a concise assistant response to the latest user message.";
        }

        return """
               [Direct response retry]
               Compatible runtime returned no visible text on the prior attempt.
               Respond directly as plain assistant text.
               Do not call tools.
               Do not include internal markers.

               User request:
               """ + normalizedRequest;
    }

    internal static bool LooksLikeRuntimeControlPayloadArtifact(string? text) {
        var candidate = (text ?? string.Empty).Trim();
        if (candidate.Length == 0 || candidate.Length > 1200) {
            return false;
        }

        if (candidate.Contains("<|channel|>", StringComparison.OrdinalIgnoreCase)
            && candidate.Contains("<|message|>", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (candidate.Contains("<|constrain|>", StringComparison.OrdinalIgnoreCase)
            && candidate.Contains("<|message|>", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return candidate.Contains("commentary to=", StringComparison.OrdinalIgnoreCase)
               && candidate.Contains("<|constrain|>", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ResolveMaxCandidateToolsSetting(int? requestedLimit, OpenAITransportKind transportKind) {
        if (requestedLimit.HasValue) {
            var requested = requestedLimit.Value;
            if (requested > 0) {
                return Math.Min(requested, ChatRequestOptionLimits.MaxCandidateTools);
            }
        }

        // Compatible-http local runtimes (LM Studio/Ollama-style endpoints) often run with smaller loaded
        // context windows than cloud providers. Keep tool candidate exposure tighter by default so routine
        // non-tool prompts do not flood the context with large function schemas.
        if (transportKind == OpenAITransportKind.CompatibleHttp) {
            return 8;
        }

        return null;
    }

    private bool ShouldDisableToolsForSelectedModel(OpenAITransportKind transportKind, string? selectedModel) {
        if (transportKind != OpenAITransportKind.CompatibleHttp) {
            return false;
        }

        if (!IsLikelyLmStudioBaseUrl(_options.OpenAIBaseUrl)) {
            return false;
        }

        var requestedModel = (selectedModel ?? string.Empty).Trim();
        if (requestedModel.Length == 0) {
            return false;
        }

        ModelListResult? modelList = null;
        lock (_modelListCacheLock) {
            modelList = _modelListCache?.Result;
        }

        if (modelList is null || modelList.Models.Count == 0) {
            return false;
        }

        var modelInfo = FindModelInfo(modelList.Models, requestedModel);
        if (modelInfo is null) {
            return false;
        }

        if (modelInfo.Capabilities.Count == 0) {
            return true;
        }

        for (var i = 0; i < modelInfo.Capabilities.Count; i++) {
            if (string.Equals(modelInfo.Capabilities[i], "tool_use", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }

        return true;
    }

    private static ModelInfo? FindModelInfo(IReadOnlyList<ModelInfo> models, string selectedModel) {
        for (var i = 0; i < models.Count; i++) {
            var model = models[i];
            if (string.Equals(model.Id, selectedModel, StringComparison.OrdinalIgnoreCase)
                || string.Equals(model.Model, selectedModel, StringComparison.OrdinalIgnoreCase)) {
                return model;
            }
        }

        return null;
    }

    private static bool IsLikelyLmStudioBaseUrl(string? baseUrl) {
        if (string.IsNullOrWhiteSpace(baseUrl)) {
            return false;
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) || uri is null) {
            return false;
        }

        if (uri.Port == 1234) {
            return true;
        }

        return uri.Host.IndexOf("lmstudio", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static ReasoningEffort? ResolveReasoningEffort(string? value, ReasoningEffort? fallback) {
        if (value is null) {
            return fallback;
        }

        var normalized = value.Trim();
        if (normalized.Length == 0) {
            return null;
        }

        return ChatEnumParser.ParseReasoningEffort(normalized) ?? fallback;
    }

    private static ReasoningSummary? ResolveReasoningSummary(string? value, ReasoningSummary? fallback) {
        if (value is null) {
            return fallback;
        }

        var normalized = value.Trim();
        if (normalized.Length == 0) {
            return null;
        }

        return ChatEnumParser.ParseReasoningSummary(normalized) ?? fallback;
    }

    private static TextVerbosity? ResolveTextVerbosity(string? value, TextVerbosity? fallback) {
        if (value is null) {
            return fallback;
        }

        var normalized = value.Trim();
        if (normalized.Length == 0) {
            return null;
        }

        return ChatEnumParser.ParseTextVerbosity(normalized) ?? fallback;
    }

}
