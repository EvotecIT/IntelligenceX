using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    private void ClearToolCatalogCache(bool clearCatalogMetadata) {
        if (clearCatalogMetadata) {
            _toolCatalogPacks = Array.Empty<ToolPackInfoDto>();
            _toolCatalogRoutingCatalog = null;
            _toolCatalogCapabilitySnapshot = null;
        }

        _toolStates.Clear();
        _toolDisplayNames.Clear();
        _toolDescriptions.Clear();
        _toolCategories.Clear();
        _toolTags.Clear();
        _toolPackIds.Clear();
        _toolPackNames.Clear();
        _toolCatalogDefinitions.Clear();
        _toolParameters.Clear();
        _toolWriteCapabilities.Clear();
        _toolExecutionAwareness.Clear();
        _toolExecutionContractIds.Clear();
        _toolExecutionScopes.Clear();
        _toolSupportsLocalExecution.Clear();
        _toolSupportsRemoteExecution.Clear();
        _toolRoutingConfidence.Clear();
        _toolRoutingReason.Clear();
        _toolRoutingScore.Clear();
        _toolStateHiddenWithoutCatalogLastCount = -1;
    }

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

    private void UpdateToolCatalog(
        ToolDefinitionDto[] tools,
        SessionRoutingCatalogDiagnosticsDto? routingCatalog = null,
        ToolPackInfoDto[]? packs = null,
        SessionCapabilitySnapshotDto? capabilitySnapshot = null) {
        _toolCatalogPacks = packs ?? Array.Empty<ToolPackInfoDto>();
        _toolCatalogRoutingCatalog = routingCatalog;
        _toolCatalogCapabilitySnapshot = capabilitySnapshot;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in tools) {
            if (string.IsNullOrWhiteSpace(tool.Name)) {
                continue;
            }

            var normalizedTool = NormalizeToolDefinitionForUiState(tool);
            var name = normalizedTool.Name;
            seen.Add(name);
            _toolCatalogDefinitions[name] = normalizedTool;
            _toolDescriptions[name] = normalizedTool.Description;
            _toolDisplayNames[name] = string.IsNullOrWhiteSpace(normalizedTool.DisplayName) ? FormatToolDisplayName(name) : normalizedTool.DisplayName.Trim();
            _toolCategories[name] = string.IsNullOrWhiteSpace(normalizedTool.Category) ? "other" : normalizedTool.Category.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedTool.PackId)) {
                var normalizedPackId = NormalizeRuntimePackId(normalizedTool.PackId);
                if (normalizedPackId.Length > 0) {
                    _toolPackIds[name] = normalizedPackId;
                } else {
                    _toolPackIds.Remove(name);
                }
            } else {
                _toolPackIds.Remove(name);
            }
            if (!string.IsNullOrWhiteSpace(normalizedTool.PackName)) {
                _toolPackNames[name] = ToolPackMetadataNormalizer.ResolveDisplayName(normalizedTool.PackId, normalizedTool.PackName);
            } else {
                _toolPackNames.Remove(name);
            }
            _toolTags[name] = NormalizeTags(normalizedTool.Tags);
            _toolParameters[name] = normalizedTool.Parameters is { Length: > 0 } parameters
                ? parameters
                : Array.Empty<ToolParameterDto>();
            _toolWriteCapabilities[name] = normalizedTool.IsWriteCapable;
            _toolExecutionAwareness[name] = normalizedTool.IsExecutionAware;
            if (!string.IsNullOrWhiteSpace(normalizedTool.ExecutionContractId)) {
                _toolExecutionContractIds[name] = normalizedTool.ExecutionContractId.Trim();
            } else {
                _toolExecutionContractIds.Remove(name);
            }
            _toolExecutionScopes[name] = ResolveToolExecutionScope(
                normalizedTool.ExecutionScope,
                normalizedTool.SupportsLocalExecution,
                normalizedTool.SupportsRemoteExecution);
            _toolSupportsLocalExecution[name] = normalizedTool.SupportsLocalExecution;
            _toolSupportsRemoteExecution[name] = normalizedTool.SupportsRemoteExecution;
            if (!_toolStates.ContainsKey(name)) {
                _toolStates[name] = !normalizedTool.IsWriteCapable;
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
                _toolCatalogDefinitions.Remove(toolName);
                _toolTags.Remove(toolName);
                _toolParameters.Remove(toolName);
                _toolWriteCapabilities.Remove(toolName);
                _toolExecutionAwareness.Remove(toolName);
                _toolExecutionContractIds.Remove(toolName);
                _toolExecutionScopes.Remove(toolName);
                _toolSupportsLocalExecution.Remove(toolName);
                _toolSupportsRemoteExecution.Remove(toolName);
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

    private static string ResolveToolExecutionScope(
        string? executionScope,
        bool supportsLocalExecution,
        bool supportsRemoteExecution) {
        var normalized = (executionScope ?? string.Empty).Trim().ToLowerInvariant();
        if (string.Equals(normalized, "local_only", StringComparison.Ordinal)
            || string.Equals(normalized, "remote_only", StringComparison.Ordinal)
            || string.Equals(normalized, "local_or_remote", StringComparison.Ordinal)) {
            return normalized;
        }

        if (supportsRemoteExecution && !supportsLocalExecution) {
            return "remote_only";
        }

        return supportsRemoteExecution ? "local_or_remote" : "local_only";
    }

    private async Task<bool> SetToolPackEnabledAsync(string packId, bool enabled) {
        var normalizedUiPackId = NormalizeUiPackId(packId);
        if (normalizedUiPackId.Length == 0) {
            return false;
        }

        var runtimePackId = NormalizeRuntimePackId(packId);
        if (runtimePackId.Length > 0 && FindSessionPackInfo(runtimePackId) is not null) {
            return await TryApplyRuntimePackSettingAsync(runtimePackId, enabled).ConfigureAwait(false);
        }

        return SetToolPackToolStateEnabled(normalizedUiPackId, enabled);
    }

    private bool SetToolPackToolStateEnabled(string normalizedPackId, bool enabled) {
        var changed = false;
        var names = new List<string>(_toolStates.Keys);
        for (var i = 0; i < names.Count; i++) {
            var toolName = names[i];
            var toolPackId = ResolveToolPackId(toolName);
            if (!string.Equals(NormalizeUiPackId(toolPackId), normalizedPackId, StringComparison.Ordinal)) {
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
            if (string.Equals(NormalizeRuntimePackId(pack.Id), normalizedPackId, StringComparison.Ordinal)) {
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
            var normalizedPackId = NormalizeRuntimePackId(explicitPackId);
            if (normalizedPackId.Length > 0) {
                return normalizedPackId;
            }
        }

        if (_toolCategories.TryGetValue(toolName, out var category) && !string.IsNullOrWhiteSpace(category)) {
            var fromCategory = NormalizeUiPackId(category);
            if (!string.IsNullOrWhiteSpace(fromCategory) && !string.Equals(fromCategory, "other", StringComparison.OrdinalIgnoreCase)) {
                return fromCategory;
            }
        }

        return "uncategorized";
    }

    private static ToolDefinitionDto NormalizeToolDefinitionForUiState(ToolDefinitionDto tool) {
        var normalizedName = tool.Name.Trim();
        var normalizedPackId = NormalizeRuntimePackId(tool.PackId);
        var normalizedPackName = ToolPackMetadataNormalizer.ResolveDisplayName(normalizedPackId, tool.PackName);
        return tool with {
            Name = normalizedName,
            Description = tool.Description ?? string.Empty,
            DisplayName = string.IsNullOrWhiteSpace(tool.DisplayName) ? null : tool.DisplayName.Trim(),
            Category = string.IsNullOrWhiteSpace(tool.Category) ? "other" : tool.Category.Trim(),
            Tags = NormalizeTags(tool.Tags),
            PackId = normalizedPackId.Length == 0 ? null : normalizedPackId,
            PackName = string.IsNullOrWhiteSpace(normalizedPackName) ? null : normalizedPackName,
            PackDescription = string.IsNullOrWhiteSpace(tool.PackDescription) ? null : tool.PackDescription.Trim(),
            ExecutionScope = NormalizeExecutionScope(tool.ExecutionScope),
            TargetScopeArguments = NormalizeStringArray(tool.TargetScopeArguments),
            RemoteHostArguments = NormalizeStringArray(tool.RemoteHostArguments),
            RepresentativeExamples = NormalizeStringArray(tool.RepresentativeExamples),
            SetupToolName = string.IsNullOrWhiteSpace(tool.SetupToolName) ? null : tool.SetupToolName.Trim(),
            HandoffTargetPackIds = NormalizePackIdArray(tool.HandoffTargetPackIds),
            HandoffTargetToolNames = NormalizeStringArray(tool.HandoffTargetToolNames),
            RecoveryToolNames = NormalizeStringArray(tool.RecoveryToolNames),
            RequiredArguments = NormalizeStringArray(tool.RequiredArguments),
            ParametersJson = string.IsNullOrWhiteSpace(tool.ParametersJson) ? "{}" : tool.ParametersJson,
            Parameters = tool.Parameters is { Length: > 0 } parameters
                ? parameters
                : Array.Empty<ToolParameterDto>(),
            MaxRetryAttempts = Math.Max(0, tool.MaxRetryAttempts)
        };
    }

    private static string[] NormalizePackIdArray(string[]? packIds) {
        if (packIds is null || packIds.Length == 0) {
            return Array.Empty<string>();
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packId in packIds) {
            var normalizedPackId = NormalizeRuntimePackId(packId);
            if (normalizedPackId.Length == 0) {
                continue;
            }

            set.Add(normalizedPackId);
        }

        if (set.Count == 0) {
            return Array.Empty<string>();
        }

        var list = new List<string>(set);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list.ToArray();
    }

    private static string NormalizeExecutionScope(string? executionScope) {
        var normalized = (executionScope ?? string.Empty).Trim().ToLowerInvariant();
        return normalized;
    }

    private static string NormalizeRuntimePackId(string? packId) {
        return ToolPackMetadataNormalizer.NormalizePackId(packId);
    }

    private static string NormalizeUiPackId(string? packId) {
        var normalized = NormalizeRuntimePackId(packId);
        if (normalized.Length == 0) {
            return string.Empty;
        }

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

            var packId = NormalizeRuntimePackId(rawPackId);
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
        return BuildMissingOnboardingFields(
            GetEffectiveUserName(),
            GetEffectiveAssistantPersona(),
            GetEffectiveThemePreset(),
            _appState.OnboardingCompleted);
    }

    internal static List<string> BuildMissingOnboardingFields(
        string? effectiveUserName,
        string? effectiveAssistantPersona,
        string? effectiveThemePreset,
        bool onboardingCompleted) {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(effectiveUserName)) {
            missing.Add("userName");
        }
        if (string.IsNullOrWhiteSpace(effectiveAssistantPersona)) {
            missing.Add("assistantPersona");
        }
        if (string.IsNullOrWhiteSpace(effectiveThemePreset)
            || (!onboardingCompleted && string.Equals(effectiveThemePreset, "default", StringComparison.OrdinalIgnoreCase))) {
            missing.Add("themePreset");
        }
        return missing;
    }

    private string BuildKickoffRequestText(IReadOnlyList<string> missingFields) {
        return PromptMarkdownBuilder.BuildKickoffRequest(missingFields);
    }

}
