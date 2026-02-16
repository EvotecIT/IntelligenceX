using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Markdown;
using IntelligenceX.Chat.Client;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OfficeIMO.MarkdownRenderer;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private const int SafeDefaultMaxToolRounds = 24;
    private const bool SafeDefaultParallelTools = true;
    private const string ParallelToolModeAuto = "auto";
    private const string ParallelToolModeForceSerial = "force_serial";
    private const string ParallelToolModeAllowParallel = "allow_parallel";
    private const int SafeDefaultTurnTimeoutSeconds = 180;
    private const int SafeDefaultToolTimeoutSeconds = 60;
    private static readonly StringComparer MemoryTokenComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly Regex MemoryTokenSplitRegex = new(@"[^\p{L}\p{Nd}_]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private void ReplaceLastAssistantText(string text) {
        ReplaceLastAssistantText(GetActiveConversation(), text);
    }

    private static void ReplaceLastAssistantText(ConversationRuntime conversation, string text) {
        for (var i = conversation.Messages.Count - 1; i >= 0; i--) {
            if (string.Equals(conversation.Messages[i].Role, "Assistant", StringComparison.Ordinal)) {
                conversation.Messages[i] = ("Assistant", text, conversation.Messages[i].Time);
                return;
            }
        }
    }

    private void UpdateToolCatalog(ToolDefinitionDto[] tools) {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in tools) {
            if (string.IsNullOrWhiteSpace(tool.Name)) {
                continue;
            }

            var name = tool.Name.Trim();
            seen.Add(name);
            _toolDescriptions[name] = tool.Description ?? string.Empty;
            _toolDisplayNames[name] = string.IsNullOrWhiteSpace(tool.DisplayName) ? FormatToolDisplayName(name) : tool.DisplayName.Trim();
            _toolCategories[name] = string.IsNullOrWhiteSpace(tool.Category) ? InferToolCategory(name) : tool.Category.Trim();
            if (!string.IsNullOrWhiteSpace(tool.PackId)) {
                _toolPackIds[name] = tool.PackId.Trim();
            } else {
                _toolPackIds.Remove(name);
            }
            if (!string.IsNullOrWhiteSpace(tool.PackName)) {
                _toolPackNames[name] = ResolvePackDisplayName(tool.PackId, tool.PackName);
            } else {
                _toolPackNames.Remove(name);
            }
            _toolTags[name] = NormalizeTags(tool.Tags);
            _toolParameters[name] = tool.Parameters is { Length: > 0 } parameters
                ? parameters
                : Array.Empty<ToolParameterDto>();
            if (!_toolStates.ContainsKey(name)) {
                _toolStates[name] = true;
            }

            // Reset routing snapshot on catalog refresh so UI never shows stale confidence/score/reason.
            _toolRoutingConfidence.Remove(name);
            _toolRoutingReason.Remove(name);
            _toolRoutingScore.Remove(name);
        }

        var existing = new List<string>(_toolStates.Keys);
        foreach (var toolName in existing) {
            if (!seen.Contains(toolName)) {
                _toolStates.Remove(toolName);
                _toolDescriptions.Remove(toolName);
                _toolDisplayNames.Remove(toolName);
                _toolCategories.Remove(toolName);
                _toolPackIds.Remove(toolName);
                _toolPackNames.Remove(toolName);
                _toolTags.Remove(toolName);
                _toolParameters.Remove(toolName);
                _toolRoutingConfidence.Remove(toolName);
                _toolRoutingReason.Remove(toolName);
                _toolRoutingScore.Remove(toolName);
            }
        }
    }

    private void SetToolEnabled(string toolName, bool enabled) {
        if (string.IsNullOrWhiteSpace(toolName)) {
            return;
        }

        var key = toolName.Trim();
        if (!_toolStates.ContainsKey(key)) {
            _toolStates[key] = true;
        }
        _toolStates[key] = enabled;
    }

    private bool SetToolPackEnabled(string packId, bool enabled) {
        var normalizedPackId = NormalizePackId(packId);
        if (normalizedPackId.Length == 0) {
            return false;
        }

        var changed = false;
        var names = new List<string>(_toolStates.Keys);
        for (var i = 0; i < names.Count; i++) {
            var toolName = names[i];
            var toolPackId = ResolveToolPackId(toolName);
            if (!string.Equals(NormalizePackId(toolPackId), normalizedPackId, StringComparison.Ordinal)) {
                continue;
            }

            if (_toolStates[toolName] == enabled) {
                continue;
            }

            _toolStates[toolName] = enabled;
            changed = true;
        }

        return changed;
    }

    private string ResolveToolPackId(string toolName) {
        if (string.IsNullOrWhiteSpace(toolName)) {
            return "other";
        }

        if (_toolPackIds.TryGetValue(toolName, out var explicitPackId) && !string.IsNullOrWhiteSpace(explicitPackId)) {
            return explicitPackId.Trim();
        }

        if (_toolCategories.TryGetValue(toolName, out var category) && !string.IsNullOrWhiteSpace(category)) {
            var fromCategory = MapCategoryToPackId(category);
            if (!string.IsNullOrWhiteSpace(fromCategory)) {
                return fromCategory;
            }
        }

        var lower = toolName.Trim().ToLowerInvariant();
        return lower switch {
            _ when lower.StartsWith("ad_", StringComparison.Ordinal) => "ad",
            _ when lower.StartsWith("eventlog_", StringComparison.Ordinal) => "eventlog",
            _ when lower.StartsWith("system_", StringComparison.Ordinal) => "system",
            _ when lower.StartsWith("wsl_", StringComparison.Ordinal) => "system",
            _ when lower.StartsWith("fs_", StringComparison.Ordinal) => "fs",
            _ when lower.StartsWith("email_", StringComparison.Ordinal) => "email",
            _ when lower.StartsWith("testimox_", StringComparison.Ordinal) => "testimox",
            _ when lower.StartsWith("reviewer_setup_", StringComparison.Ordinal) => "reviewer-setup",
            _ when lower.StartsWith("export_", StringComparison.Ordinal) => "export",
            _ => "other"
        };
    }

    private static string MapCategoryToPackId(string category) {
        return NormalizePackId(category) switch {
            "ad" => "ad",
            "active-directory" => "ad",
            "eventlog" => "eventlog",
            "event-log" => "eventlog",
            "fs" => "fs",
            "file-system" => "fs",
            "system" => "system",
            "email" => "email",
            "testimox" => "testimox",
            "reviewer-setup" => "reviewer-setup",
            "export" => "export",
            _ => string.Empty
        };
    }

    private static string NormalizePackId(string? packId) {
        var normalized = (packId ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = normalized
            .Replace("_", "-", StringComparison.Ordinal)
            .Replace(".", "-", StringComparison.Ordinal)
            .Replace(" ", "-", StringComparison.Ordinal);

        if (normalized.StartsWith("ix-", StringComparison.Ordinal)) {
            normalized = normalized[3..];
        } else if (normalized.StartsWith("intelligencex-", StringComparison.Ordinal)) {
            normalized = normalized["intelligencex-".Length..];
        }

        return normalized switch {
            "active-directory" => "ad",
            "activedirectory" => "ad",
            "adplayground" => "ad",
            "computerx" => "system",
            "event-log" => "eventlog",
            "filesystem" => "fs",
            "file-system" => "fs",
            "reviewersetup" => "reviewer-setup",
            "reviewer-setup" => "reviewer-setup",
            _ => normalized
        };
    }

    private async Task SetTimeModeAsync(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }

        var normalized = value.Trim();
        _timestampMode = ResolveTimestampMode(normalized);
        _timestampFormat = ResolveTimestampFormat(normalized);
        await RenderTranscriptAsync().ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task SetThemePresetAsync(string value) {
        var normalizedTheme = NormalizeTheme(value) ?? "default";
        if (string.Equals(_themePreset, normalizedTheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_appState.ThemePreset, normalizedTheme, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(_sessionThemeOverride)) {
            return;
        }

        _sessionThemeOverride = null;
        _themePreset = normalizedTheme;
        _appState.ThemePreset = normalizedTheme;
        await ApplyThemeFromStateAsync().ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private ChatRequestOptions? BuildChatRequestOptions() {
        var disabled = new List<string>();
        if (_toolStates.Count > 0) {
            foreach (var pair in _toolStates) {
                if (!pair.Value) {
                    disabled.Add(pair.Key);
                }
            }
        }
        if (disabled.Count > 0) {
            disabled.Sort(StringComparer.OrdinalIgnoreCase);
        }

        var effectiveMaxToolRounds = _autonomyMaxToolRounds
            ?? NormalizeAutonomyInt(_sessionPolicy?.MaxToolRounds, min: 1, max: 64)
            ?? SafeDefaultMaxToolRounds;

        var serviceDefaultParallelTools = _sessionPolicy?.ParallelTools ?? SafeDefaultParallelTools;
        var parallelToolMode = ResolveParallelToolMode(_autonomyParallelTools);
        var effectiveParallelTools = ResolveParallelToolsForRequest(parallelToolMode, serviceDefaultParallelTools);

        var effectiveTurnTimeoutSeconds = _autonomyTurnTimeoutSeconds
            ?? NormalizePositiveTimeout(_sessionPolicy?.TurnTimeoutSeconds)
            ?? SafeDefaultTurnTimeoutSeconds;

        var effectiveToolTimeoutSeconds = _autonomyToolTimeoutSeconds
            ?? NormalizePositiveTimeout(_sessionPolicy?.ToolTimeoutSeconds)
            ?? SafeDefaultToolTimeoutSeconds;

        return new ChatRequestOptions {
            Model = string.IsNullOrWhiteSpace(_localProviderModel) ? null : _localProviderModel,
            DisabledTools = disabled.Count == 0 ? null : disabled.ToArray(),
            MaxToolRounds = effectiveMaxToolRounds,
            ParallelTools = effectiveParallelTools,
            ParallelToolMode = parallelToolMode,
            TurnTimeoutSeconds = effectiveTurnTimeoutSeconds,
            ToolTimeoutSeconds = effectiveToolTimeoutSeconds,
            WeightedToolRouting = _autonomyWeightedToolRouting,
            MaxCandidateTools = _autonomyMaxCandidateTools,
            PlanExecuteReviewLoop = _autonomyPlanExecuteReviewLoop,
            MaxReviewPasses = _autonomyMaxReviewPasses,
            ModelHeartbeatSeconds = _autonomyModelHeartbeatSeconds
        };
    }

    private async Task SetAutonomyOverridesAsync(string? maxRounds, string? parallelMode, string? turnTimeout, string? toolTimeout,
        string? weightedRouting, string? maxCandidates, string? planExecuteReviewLoop, string? maxReviewPasses, string? modelHeartbeatSeconds) {
        _autonomyMaxToolRounds = ParseAutonomyInt(maxRounds, min: 1, max: 64);
        _autonomyParallelTools = ParseAutonomyParallelToolMode(parallelMode);
        _autonomyTurnTimeoutSeconds = ParseAutonomyInt(turnTimeout, min: 0, max: 3600);
        _autonomyToolTimeoutSeconds = ParseAutonomyInt(toolTimeout, min: 0, max: 3600);
        _autonomyWeightedToolRouting = ParseAutonomyParallelMode(weightedRouting);
        _autonomyMaxCandidateTools = ParseAutonomyInt(maxCandidates, min: 0, max: 64);
        _autonomyPlanExecuteReviewLoop = ParseAutonomyParallelMode(planExecuteReviewLoop);
        _autonomyMaxReviewPasses = ParseAutonomyInt(maxReviewPasses, min: 0, max: 3);
        _autonomyModelHeartbeatSeconds = ParseAutonomyInt(modelHeartbeatSeconds, min: 0, max: 60);

        _appState.AutonomyMaxToolRounds = _autonomyMaxToolRounds;
        _appState.AutonomyParallelTools = _autonomyParallelTools;
        _appState.AutonomyTurnTimeoutSeconds = _autonomyTurnTimeoutSeconds;
        _appState.AutonomyToolTimeoutSeconds = _autonomyToolTimeoutSeconds;
        _appState.AutonomyWeightedToolRouting = _autonomyWeightedToolRouting;
        _appState.AutonomyMaxCandidateTools = _autonomyMaxCandidateTools;
        _appState.AutonomyPlanExecuteReviewLoop = _autonomyPlanExecuteReviewLoop;
        _appState.AutonomyMaxReviewPasses = _autonomyMaxReviewPasses;
        _appState.AutonomyModelHeartbeatSeconds = _autonomyModelHeartbeatSeconds;

        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task ResetAutonomyOverridesAsync() {
        _autonomyMaxToolRounds = null;
        _autonomyParallelTools = null;
        _autonomyTurnTimeoutSeconds = null;
        _autonomyToolTimeoutSeconds = null;
        _autonomyWeightedToolRouting = null;
        _autonomyMaxCandidateTools = null;
        _autonomyPlanExecuteReviewLoop = null;
        _autonomyMaxReviewPasses = null;
        _autonomyModelHeartbeatSeconds = null;

        _appState.AutonomyMaxToolRounds = null;
        _appState.AutonomyParallelTools = null;
        _appState.AutonomyTurnTimeoutSeconds = null;
        _appState.AutonomyToolTimeoutSeconds = null;
        _appState.AutonomyWeightedToolRouting = null;
        _appState.AutonomyMaxCandidateTools = null;
        _appState.AutonomyPlanExecuteReviewLoop = null;
        _appState.AutonomyMaxReviewPasses = null;
        _appState.AutonomyModelHeartbeatSeconds = null;

        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task SetProactiveModeAsync(bool enabled) {
        if (_proactiveModeEnabled == enabled && _appState.ProactiveModeEnabled == enabled) {
            await PublishOptionsStateAsync().ConfigureAwait(false);
            return;
        }

        _proactiveModeEnabled = enabled;
        _appState.ProactiveModeEnabled = enabled;
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task SetQueueAutoDispatchAsync(bool enabled) {
        if (_queueAutoDispatchEnabled == enabled && _appState.QueueAutoDispatchEnabled == enabled) {
            await PublishOptionsStateAsync().ConfigureAwait(false);
            await PublishSessionStateAsync().ConfigureAwait(false);
            return;
        }

        _queueAutoDispatchEnabled = enabled;
        _appState.QueueAutoDispatchEnabled = enabled;
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
        await PublishSessionStateAsync().ConfigureAwait(false);

        var queuedTotal = GetQueuedTurnCount() + GetQueuedPromptAfterLoginCount();
        if (!enabled) {
            if (queuedTotal > 0) {
                await SetStatusAsync($"Queued turns paused ({queuedTotal} waiting).").ConfigureAwait(false);
            }
            return;
        }

        if (!_isSending) {
            await DispatchNextQueuedTurnAsync(honorAutoDispatch: false).ConfigureAwait(false);
        }
    }

    private static int? ParseAutonomyInt(string? raw, int min, int max) {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0) {
            return null;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) {
            return null;
        }

        if (parsed < min || parsed > max) {
            return null;
        }

        return parsed;
    }

    private static string NormalizeLocalProviderTransport(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "native" => TransportNative,
            "compatible-http" => TransportCompatibleHttp,
            "compatiblehttp" => TransportCompatibleHttp,
            "http" => TransportCompatibleHttp,
            "local" => TransportCompatibleHttp,
            "ollama" => TransportCompatibleHttp,
            "lmstudio" => TransportCompatibleHttp,
            "lm-studio" => TransportCompatibleHttp,
            _ => TransportNative
        };
    }

    private static string? NormalizeLocalProviderBaseUrl(string? value, string transport, string? transportHint = null) {
        var normalized = (value ?? string.Empty).Trim();
        if (!string.Equals(transport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (normalized.Length == 0) {
            var hint = (transportHint ?? string.Empty).Trim().ToLowerInvariant();
            if (hint is "lmstudio" or "lm-studio") {
                return DefaultLmStudioBaseUrl;
            }
            return DefaultOllamaBaseUrl;
        }

        return normalized;
    }

    private static string NormalizeLocalProviderModel(string? value, string transport) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length > 0) {
            return normalized;
        }

        if (string.Equals(transport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }

        return DefaultLocalModel;
    }

    private static string? NormalizeLocalProviderApiKey(string? value, string transport) {
        if (!string.Equals(transport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static bool? ParseAutonomyParallelMode(string? raw) {
        var text = (raw ?? string.Empty).Trim();
        if (text.Equals("on", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        if (text.Equals("off", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        return null;
    }

    private static bool? ParseAutonomyParallelToolMode(string? raw) {
        var text = (raw ?? string.Empty).Trim();
        return text.ToLowerInvariant() switch {
            "auto" => null,
            "default" => null,
            "allow_parallel" => true,
            "allow-parallel" => true,
            "allowparallel" => true,
            "on" => true,
            "force_serial" => false,
            "force-serial" => false,
            "forceserial" => false,
            "serial" => false,
            "off" => false,
            _ => null
        };
    }

    private static string ResolveParallelToolMode(bool? overrideParallelTools) {
        return overrideParallelTools switch {
            true => ParallelToolModeAllowParallel,
            false => ParallelToolModeForceSerial,
            _ => ParallelToolModeAuto
        };
    }

    private static bool ResolveParallelToolsForRequest(string parallelToolMode, bool serviceDefaultParallelTools) {
        return parallelToolMode switch {
            ParallelToolModeAllowParallel => true,
            ParallelToolModeForceSerial => false,
            _ => serviceDefaultParallelTools
        };
    }

    private static int? NormalizePositiveTimeout(int? value) {
        if (!value.HasValue || value.Value <= 0) {
            return null;
        }

        return value.Value;
    }

    private List<string> BuildMissingOnboardingFields() {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(GetEffectiveUserName())) {
            missing.Add("userName");
        }
        if (string.IsNullOrWhiteSpace(GetEffectiveAssistantPersona())) {
            missing.Add("assistantPersona");
        }
        var effectiveTheme = GetEffectiveThemePreset();
        if (string.IsNullOrWhiteSpace(effectiveTheme)
            || (!_appState.OnboardingCompleted && string.Equals(effectiveTheme, "default", StringComparison.OrdinalIgnoreCase))) {
            missing.Add("themePreset");
        }
        return missing;
    }

    private string BuildKickoffRequestText(IReadOnlyList<string> missingFields) {
        return PromptMarkdownBuilder.BuildKickoffRequest(missingFields);
    }

    private string BuildRequestTextForService(string userText) {
        var activeConversation = GetActiveConversation();
        var effectivePersona = GetEffectiveAssistantPersona();
        var effectiveName = GetEffectiveUserName();
        var onboardingInProgress = !_appState.OnboardingCompleted;
        IReadOnlyList<string> missingFields = onboardingInProgress ? BuildMissingOnboardingFields() : Array.Empty<string>();
        var localContextLines = BuildLocalContextFallbackLines(activeConversation, userText);
        var memoryContextLines = BuildPersistentMemoryContextLines(userText);
        var runtimeCapabilityLines = BuildRuntimeCapabilityContextLines();
        return PromptMarkdownBuilder.BuildServiceRequest(
            userText: userText,
            effectiveName: effectiveName,
            effectivePersona: effectivePersona,
            onboardingInProgress: onboardingInProgress,
            missingOnboardingFields: missingFields,
            includeLiveProfileUpdates: MightContainProfileUpdateCue(userText),
            executionBehaviorPrompt: PromptAssets.GetExecutionBehaviorPrompt(),
            localContextLines: localContextLines,
            persistentMemoryLines: memoryContextLines,
            persistentMemoryPrompt: _persistentMemoryEnabled ? PromptAssets.GetPersistentMemoryPrompt() : string.Empty,
            runtimeCapabilityLines: runtimeCapabilityLines,
            proactiveExecutionEnabled: _proactiveModeEnabled);
    }

    private IReadOnlyList<string> BuildRuntimeCapabilityContextLines() {
        var lines = new List<string>();
        var options = BuildChatRequestOptions();
        var enabledTools = 0;
        var disabledTools = 0;
        foreach (var pair in _toolStates) {
            if (pair.Value) {
                enabledTools++;
            } else {
                disabledTools++;
            }
        }

        var transportLabel = string.Equals(_localProviderTransport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)
            ? "compatible-http"
            : "native";
        var modelLabel = string.IsNullOrWhiteSpace(_localProviderModel) ? "(default)" : _localProviderModel.Trim();
        lines.Add("Runtime transport: " + transportLabel + ", model: " + modelLabel);
        lines.Add("Tools enabled: " + enabledTools.ToString(CultureInfo.InvariantCulture)
                  + ", disabled: " + disabledTools.ToString(CultureInfo.InvariantCulture));
        if (options is not null) {
            lines.Add("Parallel tool execution: " + (options.ParallelTools ? "enabled" : "disabled")
                      + " (" + (options.ParallelToolMode ?? ParallelToolModeAuto) + ")");
            lines.Add("Max tool rounds: " + options.MaxToolRounds.ToString(CultureInfo.InvariantCulture));
            lines.Add("Turn timeout: " + (options.TurnTimeoutSeconds?.ToString(CultureInfo.InvariantCulture) ?? "default")
                      + "s; tool timeout: " + (options.ToolTimeoutSeconds?.ToString(CultureInfo.InvariantCulture) ?? "default") + "s");
            lines.Add("Plan/execute/review loop: "
                      + (options.PlanExecuteReviewLoop.HasValue ? (options.PlanExecuteReviewLoop.Value ? "enabled" : "disabled") : "default")
                      + "; max review passes: "
                      + (options.MaxReviewPasses?.ToString(CultureInfo.InvariantCulture) ?? "default")
                      + "; model heartbeat: "
                      + (options.ModelHeartbeatSeconds?.ToString(CultureInfo.InvariantCulture) ?? "default") + "s");
        }

        var queuedTurns = GetQueuedTurnCount();
        if (queuedTurns > 0) {
            lines.Add("Queued follow-up turns: " + queuedTurns.ToString(CultureInfo.InvariantCulture));
        }
        lines.Add("Queued turn auto-dispatch: " + (_queueAutoDispatchEnabled ? "enabled" : "paused"));

        lines.Add("Proactive execution mode: " + (_proactiveModeEnabled ? "enabled" : "disabled"));
        return lines;
    }

}
