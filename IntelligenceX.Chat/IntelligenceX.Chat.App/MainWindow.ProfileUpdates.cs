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
                conversation.Messages[i] = ("Assistant", text, conversation.Messages[i].Time, conversation.Messages[i].Model);
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

    private ChatRequestOptions? BuildChatRequestOptions(ConversationRuntime? conversation = null) {
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

        var normalizedTransport = NormalizeLocalProviderTransport(_localProviderTransport);
        var localPreset = DetectCompatibleProviderPreset(_localProviderBaseUrl);
        var isLocalCompatibleRuntime = string.Equals(normalizedTransport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)
                                       && IsLocalCompatibleRuntimePreset(localPreset);

        // Local compatible runtimes (LM Studio/Ollama) are much more sensitive to long tool/review loops.
        // Keep defaults conservative unless the user explicitly overrides autonomy settings.
        var defaultMaxToolRounds = isLocalCompatibleRuntime ? 8 : SafeDefaultMaxToolRounds;

        var effectiveMaxToolRounds = _autonomyMaxToolRounds
            ?? NormalizeAutonomyInt(_sessionPolicy?.MaxToolRounds, min: 1, max: 64)
            ?? defaultMaxToolRounds;

        var serviceDefaultParallelTools = _sessionPolicy?.ParallelTools ?? SafeDefaultParallelTools;
        var parallelToolMode = ResolveParallelToolMode(_autonomyParallelTools);
        var effectiveParallelTools = ResolveParallelToolsForRequest(parallelToolMode, serviceDefaultParallelTools);

        var effectiveTurnTimeoutSeconds = _autonomyTurnTimeoutSeconds
            ?? NormalizePositiveTimeout(_sessionPolicy?.TurnTimeoutSeconds)
            ?? SafeDefaultTurnTimeoutSeconds;

        var effectiveToolTimeoutSeconds = _autonomyToolTimeoutSeconds
            ?? NormalizePositiveTimeout(_sessionPolicy?.ToolTimeoutSeconds)
            ?? SafeDefaultToolTimeoutSeconds;
        var configuredModel = string.IsNullOrWhiteSpace(conversation?.ModelOverride)
            ? _localProviderModel
            : conversation!.ModelOverride!;
        var resolvedModel = ResolveChatRequestModelOverride(
            _localProviderTransport,
            _localProviderBaseUrl,
            configuredModel,
            _availableModels);
        var effectiveWeightedToolRouting = _autonomyWeightedToolRouting ?? (isLocalCompatibleRuntime ? false : null);
        var effectivePlanExecuteReviewLoop = _autonomyPlanExecuteReviewLoop ?? (isLocalCompatibleRuntime ? false : null);
        var effectiveMaxReviewPasses = _autonomyMaxReviewPasses ?? (isLocalCompatibleRuntime ? 0 : null);
        var effectiveModelHeartbeatSeconds = _autonomyModelHeartbeatSeconds ?? (isLocalCompatibleRuntime ? 0 : null);

        return new ChatRequestOptions {
            Model = string.IsNullOrWhiteSpace(resolvedModel) ? null : resolvedModel,
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
            WeightedToolRouting = effectiveWeightedToolRouting,
            MaxCandidateTools = _autonomyMaxCandidateTools,
            PlanExecuteReviewLoop = effectivePlanExecuteReviewLoop,
            MaxReviewPasses = effectiveMaxReviewPasses,
            ModelHeartbeatSeconds = effectiveModelHeartbeatSeconds
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
        var selectedModel = options?.Model;
        CountKnownToolStates(out var knownToolCount, out var enabledTools, out var disabledTools);

        var transportLabel = string.Equals(_localProviderTransport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)
            ? "compatible-http"
            : string.Equals(_localProviderTransport, TransportCopilotCli, StringComparison.OrdinalIgnoreCase)
                ? "copilot-cli"
                : "native";
        var modelLabel = string.IsNullOrWhiteSpace(selectedModel) ? "(provider default)" : selectedModel.Trim();
        lines.Add("Runtime transport: " + transportLabel + ", active model for this turn: " + modelLabel);
        lines.Add("Reasoning effort: " + (string.IsNullOrWhiteSpace(_localProviderReasoningEffort) ? "provider default" : _localProviderReasoningEffort)
                  + ", summary: " + (string.IsNullOrWhiteSpace(_localProviderReasoningSummary) ? "provider default" : _localProviderReasoningSummary)
                  + ", verbosity: " + (string.IsNullOrWhiteSpace(_localProviderTextVerbosity) ? "provider default" : _localProviderTextVerbosity)
                  + ", temperature: " + (_localProviderTemperature?.ToString("0.###", CultureInfo.InvariantCulture) ?? "provider default"));
        lines.Add("Reasoning controls support: " + DescribeLocalProviderReasoningSupport(_localProviderTransport, _localProviderBaseUrl));
        lines.Add("Tool availability for this turn: "
                  + DescribeTurnToolAvailability(
                      _localProviderTransport,
                      _localProviderBaseUrl,
                      selectedModel,
                      _availableModels,
                      knownToolCount,
                      enabledTools,
                      disabledTools));
        lines.Add("Configured tool packs: enabled " + enabledTools.ToString(CultureInfo.InvariantCulture)
                  + ", disabled " + disabledTools.ToString(CultureInfo.InvariantCulture));
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
        lines.Add("Assistant rule: when asked about current runtime/model/tools, answer from these runtime lines and do not infer unavailable capabilities.");
        return lines;
    }

    private void CountKnownToolStates(out int knownToolCount, out int enabledTools, out int disabledTools) {
        var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _toolDescriptions.Keys) {
            if (!string.IsNullOrWhiteSpace(key)) {
                knownNames.Add(key.Trim());
            }
        }

        if (knownNames.Count == 0) {
            foreach (var key in _toolStates.Keys) {
                if (!string.IsNullOrWhiteSpace(key)) {
                    knownNames.Add(key.Trim());
                }
            }
        }

        knownToolCount = knownNames.Count;
        enabledTools = 0;
        disabledTools = 0;
        if (knownToolCount == 0) {
            return;
        }

        foreach (var toolName in knownNames) {
            if (_toolStates.TryGetValue(toolName, out var enabled) && !enabled) {
                disabledTools++;
            } else {
                enabledTools++;
            }
        }
    }

    internal static string DescribeTurnToolAvailability(string? transport, string? baseUrl, string? selectedModel,
        IReadOnlyList<ModelInfoDto>? availableModels, int knownToolCount, int enabledTools, int disabledTools) {
        if (knownToolCount <= 0) {
            return "unknown (tool catalog is still loading for this session).";
        }

        if (enabledTools <= 0) {
            return "unavailable (all tool packs are disabled by runtime settings).";
        }

        var normalizedTransport = NormalizeLocalProviderTransport(transport);
        if (!string.Equals(normalizedTransport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)) {
            return "available (enabled tools: "
                   + enabledTools.ToString(CultureInfo.InvariantCulture)
                   + ", disabled: "
                   + disabledTools.ToString(CultureInfo.InvariantCulture)
                   + ").";
        }

        if (!IsLocalCompatibleRuntimePreset(DetectCompatibleProviderPreset(baseUrl))) {
            return "available (enabled tools: "
                   + enabledTools.ToString(CultureInfo.InvariantCulture)
                   + "; remote/provider runtime may enforce additional limits).";
        }

        var normalizedModel = (selectedModel ?? string.Empty).Trim();
        if (normalizedModel.Length == 0) {
            return "unknown until a concrete model is selected from the discovered catalog.";
        }

        var modelInfo = FindCatalogModel(availableModels, normalizedModel);
        if (modelInfo is null) {
            return "unknown for model '" + normalizedModel + "' (not present in discovered local catalog).";
        }

        var capabilities = modelInfo.Capabilities ?? Array.Empty<string>();
        if (capabilities.Length == 0) {
            return "unavailable (model '" + normalizedModel + "' does not advertise tool_use capability).";
        }

        for (var i = 0; i < capabilities.Length; i++) {
            if (string.Equals((capabilities[i] ?? string.Empty).Trim(), "tool_use", StringComparison.OrdinalIgnoreCase)) {
                return "available (model '" + normalizedModel + "' advertises tool_use; enabled tools: "
                       + enabledTools.ToString(CultureInfo.InvariantCulture)
                       + ").";
            }
        }

        return "unavailable (model '" + normalizedModel + "' does not advertise tool_use capability).";
    }

    private static ModelInfoDto? FindCatalogModel(IReadOnlyList<ModelInfoDto>? availableModels, string model) {
        if (availableModels is null || availableModels.Count == 0 || string.IsNullOrWhiteSpace(model)) {
            return null;
        }

        for (var i = 0; i < availableModels.Count; i++) {
            var entry = availableModels[i];
            var candidateModel = (entry.Model ?? string.Empty).Trim();
            var candidateId = (entry.Id ?? string.Empty).Trim();
            if (string.Equals(candidateModel, model, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidateId, model, StringComparison.OrdinalIgnoreCase)) {
                return entry;
            }
        }

        return null;
    }

}
