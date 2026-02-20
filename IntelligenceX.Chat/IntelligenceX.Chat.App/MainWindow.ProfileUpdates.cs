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
            _toolCategories[name] = string.IsNullOrWhiteSpace(tool.Category) ? "other" : tool.Category.Trim();
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

    private async Task<bool> SetToolPackEnabledAsync(string packId, bool enabled) {
        var normalizedPackId = NormalizePackId(packId);
        if (normalizedPackId.Length == 0) {
            return false;
        }

        if (IsRuntimeManagedPack(normalizedPackId)) {
            return await TryApplyRuntimePackSettingAsync(normalizedPackId, enabled).ConfigureAwait(false);
        }

        return SetToolPackToolStateEnabled(normalizedPackId, enabled);
    }

    private bool SetToolPackToolStateEnabled(string normalizedPackId, bool enabled) {
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

    private static bool IsRuntimeManagedPack(string normalizedPackId) {
        return string.Equals(normalizedPackId, "powershell", StringComparison.Ordinal)
               || string.Equals(normalizedPackId, "testimox", StringComparison.Ordinal)
               || string.Equals(normalizedPackId, "officeimo", StringComparison.Ordinal);
    }

    private ToolPackInfoDto? FindSessionPackInfo(string normalizedPackId) {
        var packs = _sessionPolicy?.Packs;
        if (packs is null || packs.Length == 0) {
            return null;
        }

        for (var i = 0; i < packs.Length; i++) {
            var pack = packs[i];
            if (string.Equals(NormalizePackId(pack.Id), normalizedPackId, StringComparison.Ordinal)) {
                return pack;
            }
        }

        return null;
    }

    private async Task<bool> TryApplyRuntimePackSettingAsync(string normalizedPackId, bool enabled) {
        if (_isSending) {
            await SetStatusAsync("Finish the active response before changing runtime pack settings.").ConfigureAwait(false);
            return false;
        }

        var pack = FindSessionPackInfo(normalizedPackId);
        if (pack is not null && pack.Enabled == enabled) {
            return false;
        }

        var showPowerShellOnboardingHint = enabled
                                           && string.Equals(normalizedPackId, "powershell", StringComparison.Ordinal)
                                           && (pack is null || !pack.Enabled);
        bool? enablePowerShellPack = null;
        bool? enableTestimoXPack = null;
        bool? enableOfficeImoPack = null;
        if (string.Equals(normalizedPackId, "powershell", StringComparison.Ordinal)) {
            enablePowerShellPack = enabled;
        } else if (string.Equals(normalizedPackId, "testimox", StringComparison.Ordinal)) {
            enableTestimoXPack = enabled;
        } else if (string.Equals(normalizedPackId, "officeimo", StringComparison.Ordinal)) {
            enableOfficeImoPack = enabled;
        }

        var liveApply = await TryApplyRuntimeSettingsLiveAsync(
                profileSaved: true,
                model: _localProviderModel,
                openAITransport: _localProviderTransport,
                openAIBaseUrl: _localProviderBaseUrl,
                openAIAuthMode: _localProviderOpenAIAuthMode,
                openAIApiKey: null,
                openAIBasicUsername: _localProviderOpenAIBasicUsername,
                openAIBasicPassword: null,
                openAIAccountId: _localProviderOpenAIAccountId,
                clearOpenAIBasicAuth: false,
                clearOpenAIApiKey: false,
                openAIStreaming: true,
                openAIAllowInsecureHttp: ShouldAllowInsecureHttp(_localProviderTransport, _localProviderBaseUrl),
                reasoningEffort: _localProviderReasoningEffort,
                reasoningSummary: _localProviderReasoningSummary,
                textVerbosity: _localProviderTextVerbosity,
                temperature: _localProviderTemperature,
                enablePowerShellPack: enablePowerShellPack,
                enableTestimoXPack: enableTestimoXPack,
                enableOfficeImoPack: enableOfficeImoPack).ConfigureAwait(false);
        if (liveApply) {
            await RefreshLocalRuntimeDetectionAsync(publishOptions: false).ConfigureAwait(false);
            await SyncConnectedServiceProfileAndModelsAsync(
                forceModelRefresh: true,
                setProfileNewThread: false,
                appendWarnings: true).ConfigureAwait(false);
        } else {
            await SetStatusAsync(
                    "Runtime pack setting couldn't be applied live. Session stayed running; review the setting and apply again.")
                .ConfigureAwait(false);
            await PublishOptionsStateAsync().ConfigureAwait(false);
        }

        if (showPowerShellOnboardingHint) {
            AppendPowerShellOnboardingHint();
        }

        return true;
    }

    private void AppendPowerShellOnboardingHint() {
        if (_powerShellOnboardingHintShownThisSession) {
            return;
        }

        _powerShellOnboardingHintShownThisSession = true;
        AppendSystem(GetActiveConversation(), """
[info] PowerShell Runtime enabled.

You can now run shell-backed diagnostics (including cmd host support) through `powershell_run`.

Quick start prompts:
- `Run powershell_environment_discover and summarize host availability + write policy.`
- `Run powershell_run with host=cmd and command='ver' using read_only intent.`
- `Run powershell_run with host=pwsh and command='Get-Service | Select-Object -First 20' using read_only intent.`
""");
    }

    private string ResolveToolPackId(string toolName) {
        if (string.IsNullOrWhiteSpace(toolName)) {
            return "uncategorized";
        }

        if (_toolPackIds.TryGetValue(toolName, out var explicitPackId) && !string.IsNullOrWhiteSpace(explicitPackId)) {
            return explicitPackId.Trim();
        }

        if (_toolCategories.TryGetValue(toolName, out var category) && !string.IsNullOrWhiteSpace(category)) {
            var fromCategory = NormalizePackId(category);
            if (!string.IsNullOrWhiteSpace(fromCategory) && !string.Equals(fromCategory, "other", StringComparison.OrdinalIgnoreCase)) {
                return fromCategory;
            }
        }

        return "uncategorized";
    }

    private static string NormalizePackId(string? packId) {
        var normalized = (packId ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = normalized
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (string.Equals(normalized, "other", StringComparison.Ordinal)) {
            return "uncategorized";
        }

        return normalized;
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
            ReasoningEffort = _localProviderReasoningEffort,
            ReasoningSummary = _localProviderReasoningSummary,
            TextVerbosity = _localProviderTextVerbosity,
            Temperature = _localProviderTemperature,
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

    internal static bool TryNormalizeLocalProviderTransport(string? value, out string transport) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized) {
            case "native":
                transport = TransportNative;
                return true;
            case "compatible-http":
            case "compatiblehttp":
            case "http":
            case "local":
            case "ollama":
            case "lmstudio":
            case "lm-studio":
                transport = TransportCompatibleHttp;
                return true;
            case "copilot":
            case "copilot-cli":
            case "github-copilot":
            case "githubcopilot":
                transport = TransportCopilotCli;
                return true;
            default:
                transport = TransportNative;
                return false;
        }
    }

    private static string NormalizeLocalProviderTransport(string? value) {
        return TryNormalizeLocalProviderTransport(value, out var normalized)
            ? normalized
            : TransportNative;
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

    private static string DetectCompatibleProviderPreset(string? baseUrl) {
        var normalized = (baseUrl ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return "manual";
        }

        if (normalized.Contains("127.0.0.1:1234", StringComparison.Ordinal)
            || normalized.Contains("localhost:1234", StringComparison.Ordinal)) {
            return "lmstudio";
        }

        if (normalized.Contains("127.0.0.1:11434", StringComparison.Ordinal)
            || normalized.Contains("localhost:11434", StringComparison.Ordinal)) {
            return "ollama";
        }

        if (normalized.Contains("api.openai.com", StringComparison.Ordinal)) {
            return "openai";
        }

        if (normalized.Contains(".openai.azure.com", StringComparison.Ordinal)) {
            return "azure-openai";
        }

        if (normalized.Contains("anthropic", StringComparison.Ordinal)
            || normalized.Contains("claude", StringComparison.Ordinal)) {
            return "anthropic-bridge";
        }

        if (normalized.Contains("gemini", StringComparison.Ordinal)
            || normalized.Contains("googleapis.com", StringComparison.Ordinal)) {
            return "gemini-bridge";
        }

        return "manual";
    }

    private static bool SupportsLocalProviderReasoningControls(string? transport, string? baseUrl) {
        var normalizedTransport = NormalizeLocalProviderTransport(transport);
        if (string.Equals(normalizedTransport, TransportCopilotCli, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        return true;
    }

    private static string DescribeLocalProviderReasoningSupport(string? transport, string? baseUrl) {
        if (SupportsLocalProviderReasoningControls(transport, baseUrl)) {
            return "enabled (pass-through; provider may clamp unsupported values)";
        }

        var normalizedTransport = NormalizeLocalProviderTransport(transport);
        if (string.Equals(normalizedTransport, TransportCopilotCli, StringComparison.OrdinalIgnoreCase)) {
            return "not exposed by Copilot subscription runtime";
        }

        return "not exposed by current runtime profile";
    }

    private static string NormalizeLocalProviderModel(string? value, string transport) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length > 0) {
            return normalized;
        }

        if (string.Equals(transport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(transport, TransportCopilotCli, StringComparison.OrdinalIgnoreCase)) {
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

    private static string NormalizeLocalProviderOpenAIAuthMode(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "basic" => "basic",
            "none" => "none",
            "off" => "none",
            "bearer" => "bearer",
            "api-key" => "bearer",
            "apikey" => "bearer",
            "token" => "bearer",
            _ => "bearer"
        };
    }

    private static string NormalizeLocalProviderOpenAIBasicUsername(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? string.Empty : normalized;
    }

    private static string? NormalizeLocalProviderOpenAIBasicPassword(string? value, string transport) {
        if (!string.Equals(transport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string NormalizeLocalProviderReasoningEffort(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "minimal" => "minimal",
            "low" => "low",
            "medium" => "medium",
            "high" => "high",
            "xhigh" => "xhigh",
            "x-high" => "xhigh",
            "x_high" => "xhigh",
            _ => string.Empty
        };
    }

    private static string NormalizeLocalProviderReasoningSummary(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "auto" => "auto",
            "concise" => "concise",
            "detailed" => "detailed",
            "off" => "off",
            _ => string.Empty
        };
    }

    private static string NormalizeLocalProviderTextVerbosity(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "low" => "low",
            "medium" => "medium",
            "high" => "high",
            _ => string.Empty
        };
    }

    private static double? NormalizeLocalProviderTemperature(double? value) {
        if (!value.HasValue) {
            return null;
        }

        var parsed = value.Value;
        if (double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < 0d || parsed > 2d) {
            return null;
        }

        return parsed;
    }

    private static double? NormalizeLocalProviderTemperature(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return null;
        }

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) {
            return null;
        }

        return NormalizeLocalProviderTemperature(parsed);
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
            : string.Equals(_localProviderTransport, TransportCopilotCli, StringComparison.OrdinalIgnoreCase)
                ? "copilot-cli"
                : "native";
        var modelLabel = string.IsNullOrWhiteSpace(_localProviderModel) ? "(default)" : _localProviderModel.Trim();
        lines.Add("Runtime transport: " + transportLabel + ", model: " + modelLabel);
        lines.Add("Reasoning effort: " + (string.IsNullOrWhiteSpace(_localProviderReasoningEffort) ? "provider default" : _localProviderReasoningEffort)
                  + ", summary: " + (string.IsNullOrWhiteSpace(_localProviderReasoningSummary) ? "provider default" : _localProviderReasoningSummary)
                  + ", verbosity: " + (string.IsNullOrWhiteSpace(_localProviderTextVerbosity) ? "provider default" : _localProviderTextVerbosity)
                  + ", temperature: " + (_localProviderTemperature?.ToString("0.###", CultureInfo.InvariantCulture) ?? "provider default"));
        lines.Add("Reasoning controls support: " + DescribeLocalProviderReasoningSupport(_localProviderTransport, _localProviderBaseUrl));
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
