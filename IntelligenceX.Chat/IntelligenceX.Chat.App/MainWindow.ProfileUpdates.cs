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
    private const int SafeDefaultMaxToolRounds = ChatRequestOptionLimits.DefaultToolRounds;
    private const int AutonomyMaxToolRoundsLimit = ChatRequestOptionLimits.MaxToolRounds;
    private const int AutonomyMaxCandidateToolsLimit = ChatRequestOptionLimits.MaxCandidateTools;
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

    private static void AppendAssistantText(ConversationRuntime conversation, string text) {
        if (conversation.Messages.Count > 0) {
            var last = conversation.Messages[^1];
            if (!ShouldAppendAssistantSnapshot(
                    candidateAssistantText: text,
                    previousRole: last.Role,
                    previousAssistantText: last.Text)) {
                return;
            }
        } else if (!ShouldAppendAssistantSnapshot(
                       candidateAssistantText: text,
                       previousRole: null,
                       previousAssistantText: null)) {
            return;
        }

        var nowLocal = DateTime.Now;
        var modelLabel = string.IsNullOrWhiteSpace(conversation.ModelLabel) ? null : conversation.ModelLabel.Trim();
        conversation.Messages.Add(("Assistant", text, nowLocal, modelLabel));
    }

    private static void ReplaceLastAssistantText(ConversationRuntime conversation, string text) {
        var nowLocal = DateTime.Now;
        var replaceIndex = ResolveAssistantReplaceIndexForUpdate(conversation.Messages);
        if (replaceIndex >= 0) {
            var existing = conversation.Messages[replaceIndex];
            var updatedTimestamp = ResolveAssistantTimestampForUpdate(
                existing.Time,
                existing.Text,
                text,
                nowLocal);
            var updatedModel = string.IsNullOrWhiteSpace(existing.Model)
                ? string.IsNullOrWhiteSpace(conversation.ModelLabel) ? null : conversation.ModelLabel.Trim()
                : existing.Model;
            conversation.Messages[replaceIndex] = ("Assistant", text, updatedTimestamp, updatedModel);
            return;
        }

        var modelLabel = string.IsNullOrWhiteSpace(conversation.ModelLabel) ? null : conversation.ModelLabel.Trim();
        conversation.Messages.Add(("Assistant", text, nowLocal, modelLabel));
    }

    internal static int ResolveAssistantReplaceIndexForUpdate(IReadOnlyList<(string Role, string Text, DateTime Time, string? Model)> messages) {
        if (messages is null || messages.Count == 0) {
            return -1;
        }

        var lastIndex = messages.Count - 1;
        if (string.Equals(messages[lastIndex].Role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
            return lastIndex;
        }

        // If no user message appeared after the most-recent assistant row, this is still the same
        // user turn progression and should update that assistant bubble instead of appending a duplicate.
        for (var i = lastIndex; i >= 0; i--) {
            var role = messages[i].Role;
            if (string.Equals(role, "User", StringComparison.OrdinalIgnoreCase)) {
                return -1;
            }

            if (string.Equals(role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }

        return -1;
    }

    internal static DateTime ResolveAssistantTimestampForUpdate(
        DateTime currentTimestamp,
        string? existingText,
        string? nextText,
        DateTime nowLocal) {
        var hadVisibleContent = !string.IsNullOrWhiteSpace(existingText);
        var hasVisibleContent = !string.IsNullOrWhiteSpace(nextText);
        if (!hadVisibleContent && hasVisibleContent) {
            return nowLocal;
        }

        return currentTimestamp;
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
            _toolWriteCapabilities[name] = tool.IsWriteCapable;
            if (!_toolStates.ContainsKey(name)) {
                _toolStates[name] = !tool.IsWriteCapable;
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
                _toolWriteCapabilities.Remove(toolName);
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
            _toolStates[key] = !IsWriteCapableTool(key);
        }
        _toolStates[key] = enabled;
    }

    private async Task<bool> SetToolPackEnabledAsync(string packId, bool enabled) {
        var normalizedPackId = NormalizePackId(packId);
        if (normalizedPackId.Length == 0) {
            return false;
        }

        if (FindSessionPackInfo(normalizedPackId) is not null) {
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
        if (IsTurnDispatchInProgress()) {
            await SetStatusAsync("Finish the active response before changing runtime pack settings.").ConfigureAwait(false);
            return false;
        }

        var pack = FindSessionPackInfo(normalizedPackId);
        if (pack is not null && pack.Enabled == enabled) {
            return false;
        }

        var enablePackIds = enabled ? new[] { normalizedPackId } : null;
        var disablePackIds = enabled ? null : new[] { normalizedPackId };

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
                enablePackIds: enablePackIds,
                disablePackIds: disablePackIds).ConfigureAwait(false);
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

        return true;
    }

    private bool IsWriteCapableTool(string toolName) {
        return _toolWriteCapabilities.TryGetValue(toolName, out var isWriteCapable) && isWriteCapable;
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

    internal static (string[] DisabledTools, string[] DisabledPackIds) BuildToolExposureOverridesForRequest(
        IReadOnlyDictionary<string, bool> toolStates,
        IReadOnlyDictionary<string, string> toolPackIds) {
        if (toolStates is null || toolStates.Count == 0) {
            return (Array.Empty<string>(), Array.Empty<string>());
        }

        var disabledTools = new List<string>(toolStates.Count);
        var toolPackByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var packTotals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var packDisabled = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in toolStates) {
            var toolName = (pair.Key ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                continue;
            }

            var isEnabled = pair.Value;
            if (!isEnabled) {
                disabledTools.Add(toolName);
            }

            if (!toolPackIds.TryGetValue(toolName, out var rawPackId)) {
                continue;
            }

            var packId = (rawPackId ?? string.Empty).Trim();
            if (packId.Length == 0) {
                continue;
            }

            toolPackByName[toolName] = packId;
            packTotals[packId] = packTotals.TryGetValue(packId, out var totalCount) ? totalCount + 1 : 1;
            if (!isEnabled) {
                packDisabled[packId] = packDisabled.TryGetValue(packId, out var disabledCount) ? disabledCount + 1 : 1;
            }
        }

        var disabledPackIds = new List<string>(packTotals.Count);
        foreach (var pair in packTotals) {
            var packId = pair.Key;
            var totalCount = pair.Value;
            if (totalCount <= 0) {
                continue;
            }

            if (packDisabled.TryGetValue(packId, out var disabledCount) && disabledCount >= totalCount) {
                disabledPackIds.Add(packId);
            }
        }

        if (disabledPackIds.Count > 1) {
            disabledPackIds.Sort(StringComparer.OrdinalIgnoreCase);
        }

        if (disabledPackIds.Count > 0 && disabledTools.Count > 0) {
            var fullyDisabledPacks = new HashSet<string>(disabledPackIds, StringComparer.OrdinalIgnoreCase);
            disabledTools.RemoveAll(toolName =>
                toolPackByName.TryGetValue(toolName, out var packId)
                && fullyDisabledPacks.Contains(packId));
        }

        if (disabledTools.Count > 1) {
            disabledTools.Sort(StringComparer.OrdinalIgnoreCase);
        }

        return (
            disabledTools.Count == 0 ? Array.Empty<string>() : disabledTools.ToArray(),
            disabledPackIds.Count == 0 ? Array.Empty<string>() : disabledPackIds.ToArray());
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
        var (disabledTools, disabledPackIds) = BuildToolExposureOverridesForRequest(_toolStates, _toolPackIds);

        var normalizedTransport = NormalizeLocalProviderTransport(_localProviderTransport);
        var localPreset = DetectCompatibleProviderPreset(_localProviderBaseUrl);
        var isLocalCompatibleRuntime = string.Equals(normalizedTransport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)
                                       && IsLocalCompatibleRuntimePreset(localPreset);

        // Local compatible runtimes (LM Studio/Ollama) are much more sensitive to long tool/review loops.
        // Keep defaults conservative unless the user explicitly overrides autonomy settings.
        // Keep local-compatible runtime conservative (8 rounds) to avoid runaway loops on weaker local transports;
        // full/default round budget applies to service/native paths.
        var defaultMaxToolRounds = isLocalCompatibleRuntime ? 8 : SafeDefaultMaxToolRounds;

        var effectiveMaxToolRounds = _autonomyMaxToolRounds
            ?? NormalizeAutonomyInt(_sessionPolicy?.MaxToolRounds, min: ChatRequestOptionLimits.MinToolRounds, max: AutonomyMaxToolRoundsLimit)
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
        // Keep conversation flow predictable by default; advanced review loops remain opt-in via autonomy controls.
        var effectivePlanExecuteReviewLoop = _autonomyPlanExecuteReviewLoop ?? false;
        var effectiveMaxReviewPasses = _autonomyMaxReviewPasses ?? 0;
        var effectiveModelHeartbeatSeconds = _autonomyModelHeartbeatSeconds ?? (isLocalCompatibleRuntime ? 0 : null);

        return new ChatRequestOptions {
            Model = string.IsNullOrWhiteSpace(resolvedModel) ? null : resolvedModel,
            ReasoningEffort = _localProviderReasoningEffort,
            ReasoningSummary = _localProviderReasoningSummary,
            TextVerbosity = _localProviderTextVerbosity,
            Temperature = _localProviderTemperature,
            DisabledTools = disabledTools.Length == 0 ? null : disabledTools,
            DisabledPackIds = disabledPackIds.Length == 0 ? null : disabledPackIds,
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
        _autonomyMaxToolRounds = ParseAutonomyInt(maxRounds, min: ChatRequestOptionLimits.MinToolRounds, max: AutonomyMaxToolRoundsLimit);
        _autonomyParallelTools = ParseAutonomyParallelToolMode(parallelMode);
        _autonomyTurnTimeoutSeconds = ParseAutonomyInt(
            turnTimeout,
            min: ChatRequestOptionLimits.MinTimeoutSeconds,
            max: ChatRequestOptionLimits.MaxTimeoutSeconds);
        _autonomyToolTimeoutSeconds = ParseAutonomyInt(
            toolTimeout,
            min: ChatRequestOptionLimits.MinTimeoutSeconds,
            max: ChatRequestOptionLimits.MaxTimeoutSeconds);
        _autonomyWeightedToolRouting = ParseAutonomyParallelMode(weightedRouting);
        _autonomyMaxCandidateTools = ParseAutonomyInt(maxCandidates,
            min: ChatRequestOptionLimits.MinCandidateTools,
            max: AutonomyMaxCandidateToolsLimit);
        _autonomyPlanExecuteReviewLoop = ParseAutonomyParallelMode(planExecuteReviewLoop);
        _autonomyMaxReviewPasses = ParseAutonomyInt(
            maxReviewPasses,
            min: 0,
            max: ChatRequestOptionLimits.MaxReviewPasses);
        _autonomyModelHeartbeatSeconds = ParseAutonomyInt(
            modelHeartbeatSeconds,
            min: 0,
            max: ChatRequestOptionLimits.MaxModelHeartbeatSeconds);

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

        if (!IsTurnDispatchInProgress()) {
            await DispatchNextQueuedTurnAsync(honorAutoDispatch: false).ConfigureAwait(false);
        }
    }

    private async Task SetShowAssistantTurnTraceAsync(bool enabled) {
        if (_showAssistantTurnTrace == enabled && _appState.ShowAssistantTurnTrace == enabled) {
            await PublishOptionsStateAsync().ConfigureAwait(false);
            return;
        }

        _showAssistantTurnTrace = enabled;
        _appState.ShowAssistantTurnTrace = enabled;
        await RenderTranscriptAsync().ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task SetShowAssistantDraftBubblesAsync(bool enabled) {
        if (_showAssistantDraftBubbles == enabled && _appState.ShowAssistantDraftBubbles == enabled) {
            await PublishOptionsStateAsync().ConfigureAwait(false);
            return;
        }

        _showAssistantDraftBubbles = enabled;
        _appState.ShowAssistantDraftBubbles = enabled;
        await RenderTranscriptAsync().ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
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
        var assistantCapabilityQuestion = ConversationTurnShapeClassifier.LooksLikeAssistantCapabilityQuestion(userText);
        var assistantRuntimeIntrospectionQuestion = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion(userText);
        var includeOnboardingContext = ShouldIncludeAmbientOnboardingContext(
            userText,
            onboardingInProgress,
            assistantCapabilityQuestion,
            assistantRuntimeIntrospectionQuestion);
        IReadOnlyList<string> missingFields = includeOnboardingContext ? BuildMissingOnboardingFields() : Array.Empty<string>();
        var localContextLines = BuildLocalContextFallbackLines(activeConversation, userText);
        var conversationStyleLines = ConversationStyleGuidanceBuilder.BuildRecentUserStyleLines(activeConversation.Messages);
        var capabilityAnswerStyleLines = assistantCapabilityQuestion
            ? ConversationStyleGuidanceBuilder.BuildCapabilityAnswerStyleLines(activeConversation.Messages)
            : null;
        var personaGuidanceLines = BuildPersonaGuidanceLines(effectivePersona);
        var continuationStateLines = ConversationStyleGuidanceBuilder.BuildContinuationStateLines(
            activeConversation.Messages,
            activeConversation.PendingActions,
            activeConversation.PendingAssistantQuestionHint);
        var recentAssistantAnswerWasSubstantive = ConversationStyleGuidanceBuilder.HasRecentSubstantiveAssistantAnswer(activeConversation.Messages);
        var recentAssistantAskedQuestion = ConversationStyleGuidanceBuilder.HasRecentAssistantQuestion(activeConversation.Messages);
        var memoryContextLines = BuildPersistentMemoryContextLines(userText);
        var capabilitySelfKnowledgeLines = assistantCapabilityQuestion || assistantRuntimeIntrospectionQuestion
            ? BuildCapabilitySelfKnowledgeLines()
            : null;
        var runtimeCapabilityLines = assistantRuntimeIntrospectionQuestion
            ? BuildRuntimeCapabilityContextLines()
            : null;
        bool? proactiveExecutionEnabled = null;
        if (_proactiveModeEnabled
            && ShouldIncludeProactiveExecutionMode(
                userText,
                assistantCapabilityQuestion,
                assistantRuntimeIntrospectionQuestion,
                recentAssistantAskedQuestion)) {
            proactiveExecutionEnabled = true;
        }

        return PromptMarkdownBuilder.BuildServiceRequest(
            userText: userText,
            effectiveName: effectiveName,
            effectivePersona: effectivePersona,
            onboardingInProgress: includeOnboardingContext,
            missingOnboardingFields: missingFields,
            includeLiveProfileUpdates: MightContainProfileUpdateCue(userText),
            executionBehaviorPrompt: PromptAssets.GetExecutionBehaviorPrompt(),
            localContextLines: localContextLines,
            conversationStyleLines: conversationStyleLines,
            capabilityAnswerStyleLines: capabilityAnswerStyleLines,
            personaGuidanceLines: personaGuidanceLines,
            continuationStateLines: continuationStateLines,
            recentAssistantAnswerWasSubstantive: recentAssistantAnswerWasSubstantive,
            recentAssistantAskedQuestion: recentAssistantAskedQuestion,
            persistentMemoryLines: memoryContextLines,
            persistentMemoryPrompt: _persistentMemoryEnabled ? PromptAssets.GetPersistentMemoryPrompt() : string.Empty,
            capabilitySelfKnowledgeLines: capabilitySelfKnowledgeLines,
            runtimeCapabilityLines: runtimeCapabilityLines,
            proactiveExecutionEnabled: proactiveExecutionEnabled);
    }

    internal static bool ShouldIncludeAmbientOnboardingContext(
        string? userText,
        bool onboardingInProgress,
        bool assistantCapabilityQuestion,
        bool assistantRuntimeIntrospectionQuestion) {
        if (!onboardingInProgress) {
            return false;
        }

        var normalized = (userText ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (assistantCapabilityQuestion || assistantRuntimeIntrospectionQuestion) {
            return false;
        }

        return ConversationTurnShapeClassifier.LooksLikeLowContextShortTurn(normalized);
    }

    internal static bool ShouldIncludeProactiveExecutionMode(
        string? userText,
        bool assistantCapabilityQuestion,
        bool assistantRuntimeIntrospectionQuestion,
        bool recentAssistantAskedQuestion) {
        var normalized = (userText ?? string.Empty).Trim();
        if (normalized.Length == 0
            || assistantCapabilityQuestion
            || assistantRuntimeIntrospectionQuestion
            || ConversationTurnShapeClassifier.LooksLikeLowContextShortTurn(normalized)) {
            return false;
        }

        if (recentAssistantAskedQuestion && ConversationTurnShapeClassifier.LooksLikeContextDependentFollowUp(normalized)) {
            return true;
        }

        return !ConversationTurnShapeClassifier.ContainsQuestionSignal(normalized);
    }

    internal IReadOnlyList<string> BuildCapabilitySelfKnowledgeLines() {
        return BuildCapabilitySelfKnowledgeLines(_sessionPolicy);
    }

    internal static IReadOnlyList<string> BuildCapabilitySelfKnowledgeLines(SessionPolicyDto? sessionPolicy) {
        var lines = new List<string>();
        var snapshot = sessionPolicy?.CapabilitySnapshot;
        var enabledPackNames = BuildEnabledPackDisplayNames(sessionPolicy);
        if (enabledPackNames.Count > 0) {
            lines.Add("Active working areas in this session: " + string.Join(", ", enabledPackNames) + ".");
        }

        var capabilityCategories = BuildCapabilityCategorySummaries(snapshot);
        for (var i = 0; i < capabilityCategories.Count; i++) {
            lines.Add(capabilityCategories[i]);
        }

        var exampleLines = BuildCapabilityExampleLines(snapshot, enabledPackNames);
        for (var i = 0; i < exampleLines.Count; i++) {
            lines.Add(exampleLines[i]);
        }

        if (snapshot is not null) {
            if (snapshot.ToolingAvailable) {
                lines.Add("You can actively use live session tools here when the user asks for checks, investigation, or data gathering.");
            } else {
                lines.Add("Tooling is not currently available in this session, so answers should stay conversational and reasoning-based.");
            }

            if (snapshot.HealthyTools.Length > 0) {
                lines.Add("Recently healthy tool count: " + snapshot.HealthyTools.Length.ToString(CultureInfo.InvariantCulture) + ".");
            }

            if (!string.IsNullOrWhiteSpace(snapshot.RemoteReachabilityMode)) {
                lines.Add("Current reachability posture: " + DescribeReachabilityMode(snapshot.RemoteReachabilityMode) + ".");
            }
        } else if (enabledPackNames.Count == 0) {
            lines.Add("Session capabilities are still loading, so avoid pretending to have tools you cannot verify.");
        }

        lines.Add("For explicit capability questions, lead with a few practical examples that are genuinely live in this session, then invite the user's task.");
        lines.Add("When asked what you can do, answer with useful examples and invite the task instead of listing internal pack ids or protocol details.");
        return lines;
    }

    internal static IReadOnlyList<string> BuildPersonaGuidanceLines(string? personaText) {
        var normalized = (personaText ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return Array.Empty<string>();
        }

        var lines = new List<string>();
        var role = NormalizePersonaRole(normalized, normalized);
        if (!string.IsNullOrWhiteSpace(role)) {
            lines.Add("Preferred role framing: " + role + ".");
        }

        var traits = CollectPersonaTraits(normalized);
        for (var i = 0; i < traits.Count; i++) {
            var trait = (traits[i] ?? string.Empty).Trim();
            if (trait.Length == 0) {
                continue;
            }

            if (string.Equals(trait, "helpful guidance", StringComparison.OrdinalIgnoreCase)) {
                lines.Add("Be proactively useful: reduce user effort, infer sensible next steps, and avoid making the user micromanage the conversation.");
            } else if (string.Equals(trait, "friendly tone", StringComparison.OrdinalIgnoreCase)) {
                lines.Add("Sound warm and human instead of dry, stiff, or corporate.");
            } else if (string.Equals(trait, "light humor", StringComparison.OrdinalIgnoreCase)) {
                lines.Add("Light humor is allowed when it fits naturally. Keep it subtle, optional, and secondary to usefulness.");
            } else if (string.Equals(trait, "concise outputs", StringComparison.OrdinalIgnoreCase)) {
                lines.Add("Prefer compact phrasing and shorter answers by default unless the user clearly wants depth.");
            } else if (string.Equals(trait, "pragmatic guidance", StringComparison.OrdinalIgnoreCase)) {
                lines.Add("Favor concrete next steps, practical judgments, and real-world usefulness over abstract theory.");
            } else if (string.Equals(trait, "clear explanations", StringComparison.OrdinalIgnoreCase)) {
                lines.Add("When explanation helps, make it clear and easy to follow instead of compressed or jargon-heavy.");
            } else if (string.Equals(trait, "optimistic tone", StringComparison.OrdinalIgnoreCase)) {
                lines.Add("Keep the tone constructive and steady without sounding fake, evasive, or over-cheerful.");
            }
        }

        return lines.Count == 0 ? Array.Empty<string>() : lines;
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
        AppendWriteToolCapabilityContextLines(lines);
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

    private void AppendWriteToolCapabilityContextLines(List<string> lines) {
        ArgumentNullException.ThrowIfNull(lines);
        if (_toolWriteCapabilities.Count == 0) {
            return;
        }

        var enabledWriteTools = new List<string>();
        var disabledWriteTools = new List<string>();
        foreach (var pair in _toolWriteCapabilities) {
            if (!pair.Value) {
                continue;
            }

            if (_toolStates.TryGetValue(pair.Key, out var enabled) && enabled) {
                enabledWriteTools.Add(pair.Key);
            } else {
                disabledWriteTools.Add(pair.Key);
            }
        }

        enabledWriteTools.Sort(StringComparer.OrdinalIgnoreCase);
        disabledWriteTools.Sort(StringComparer.OrdinalIgnoreCase);
        lines.Add("Write-capable tools: enabled " + enabledWriteTools.Count.ToString(CultureInfo.InvariantCulture)
                  + ", disabled " + disabledWriteTools.Count.ToString(CultureInfo.InvariantCulture) + ".");
        if (disabledWriteTools.Count > 0) {
            var preview = string.Join(", ", disabledWriteTools.GetRange(0, Math.Min(disabledWriteTools.Count, 8)));
            if (disabledWriteTools.Count > 8) {
                preview += ", ...";
            }
            lines.Add("Disabled write-capable tools: " + preview + ".");
        }
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

    private static List<string> BuildEnabledPackDisplayNames(SessionPolicyDto? sessionPolicy) {
        var names = new List<string>();
        var packs = sessionPolicy?.Packs;
        if (packs is not { Length: > 0 }) {
            return names;
        }

        for (var i = 0; i < packs.Length; i++) {
            var pack = packs[i];
            if (!pack.Enabled) {
                continue;
            }

            var displayName = (pack.Name ?? string.Empty).Trim();
            if (displayName.Length == 0) {
                displayName = NormalizePackId(pack.Id);
            }

            if (displayName.Length > 0 && !ContainsIgnoreCase(names, displayName)) {
                names.Add(displayName);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static List<string> BuildCapabilityCategorySummaries(SessionCapabilitySnapshotDto? snapshot) {
        var lines = new List<string>();
        if (snapshot?.FamilyActions is not { Length: > 0 }) {
            return lines;
        }

        if (HasCapabilityFamily(snapshot, "ad_domain")) {
            lines.Add("You can help with Active Directory checks such as users, groups, LDAP lookups, and domain-controller or replication-related investigation when those tools are enabled.");
        }

        if (HasCapabilityFamily(snapshot, "eventlog")) {
            lines.Add("You can inspect Windows event logs and correlate system evidence when the session has Event Log tooling available.");
        }

        if (HasCapabilityFamily(snapshot, "public_domain")) {
            lines.Add("You can investigate public-domain signals such as DNS and mail configuration when the relevant tooling is enabled.");
        }

        return lines;
    }

    private static List<string> BuildCapabilityExampleLines(SessionCapabilitySnapshotDto? snapshot, IReadOnlyList<string> enabledPackNames) {
        var lines = new List<string>();
        if (snapshot is not null && HasCapabilityFamily(snapshot, "ad_domain")) {
            lines.Add("Concrete examples you can mention: check AD replication health, find users/groups/computers, or review group membership and LDAP data.");
        }

        if (ContainsIgnoreCase(enabledPackNames, "Event Viewer")) {
            lines.Add("Concrete examples you can mention: inspect Windows event logs, summarize recurring errors, or correlate recent failures on this machine or a reachable target.");
        }

        if (snapshot is not null && HasCapabilityFamily(snapshot, "public_domain")) {
            lines.Add("Concrete examples you can mention: inspect public DNS, check MX/SPF/DMARC, or review mail-related public-domain signals.");
        }

        if (ContainsIgnoreCase(enabledPackNames, "System")) {
            lines.Add("Concrete examples you can mention: inspect local host inventory, services, scheduled tasks, or filesystem evidence under allowed roots.");
        }

        return lines;
    }

    private static bool HasCapabilityFamily(SessionCapabilitySnapshotDto snapshot, string family) {
        var expected = (family ?? string.Empty).Trim();
        if (expected.Length == 0 || snapshot.FamilyActions.Length == 0) {
            return false;
        }

        for (var i = 0; i < snapshot.FamilyActions.Length; i++) {
            var entry = snapshot.FamilyActions[i];
            if (string.Equals((entry.Family ?? string.Empty).Trim(), expected, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static string DescribeReachabilityMode(string? mode) {
        var normalized = (mode ?? string.Empty).Trim();
        if (normalized.Equals("remote_capable", StringComparison.OrdinalIgnoreCase)) {
            return "remote-capable";
        }

        if (normalized.Equals("local_only", StringComparison.OrdinalIgnoreCase)) {
            return "local-only";
        }

        return normalized.Length == 0 ? "unknown" : normalized;
    }

    private static bool ContainsIgnoreCase(IReadOnlyList<string> values, string candidate) {
        ArgumentNullException.ThrowIfNull(values);
        var normalizedCandidate = (candidate ?? string.Empty).Trim();
        if (normalizedCandidate.Length == 0) {
            return false;
        }

        for (var i = 0; i < values.Count; i++) {
            if (string.Equals((values[i] ?? string.Empty).Trim(), normalizedCandidate, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

}
