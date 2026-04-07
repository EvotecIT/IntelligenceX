using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Launch;
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
    internal enum ModelsProbeAvailability {
        Unavailable = 0,
        Available = 1,
        ReachableAuthRequired = 2
    }

    private sealed record LocalRuntimeDetectionSnapshot(
        bool LmStudioAvailable,
        bool OllamaAvailable,
        string? DetectedName,
        string? DetectedBaseUrl,
        string? Warning);

    private void UpdateRuntimeApplyProgress(string stage, string? detail, bool active, long requestId) {
        var normalizedStage = (stage ?? string.Empty).Trim().ToLowerInvariant();
        _runtimeApplyStage = normalizedStage.Length == 0 ? "idle" : normalizedStage;
        _runtimeApplyDetail = (detail ?? string.Empty).Trim();
        _runtimeApplyActive = active;
        _runtimeApplyUpdatedUtc = DateTime.UtcNow;
        _runtimeApplyRequestId = requestId > 0 ? requestId : _runtimeApplyRequestId;
    }

    private async Task RefreshModelsFromUiAsync(bool forceRefresh) {
        if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
            return;
        }

        await SyncConnectedServiceProfileAndModelsAsync(
            forceModelRefresh: forceRefresh,
            setProfileNewThread: false,
            appendWarnings: true).ConfigureAwait(false);
    }

    private async Task<bool> SyncConnectedServiceProfileAndModelsAsync(bool forceModelRefresh, bool setProfileNewThread, bool appendWarnings) {
        var client = _client;
        if (client is null) {
            return false;
        }

        await RefreshServiceProfilesAsync(client, publishOptions: false, appendWarnings).ConfigureAwait(false);
        var profileApplied = await TryApplyServiceProfileAsync(client, setProfileNewThread, appendWarnings).ConfigureAwait(false);
        await RefreshModelsAsync(client, forceModelRefresh, publishOptions: false, appendWarnings).ConfigureAwait(false);
        await RefreshBackgroundSchedulerStatusAsync(client, publishOptions: false, appendWarnings).ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);
        return profileApplied;
    }

    private bool ShouldRefreshToolCatalogOnOptionsRefresh() {
        if (!_isConnected || _client is null) {
            return false;
        }

        return _toolCatalogDefinitions.Count == 0
               || CountToolsHiddenWithoutCatalog() > 0;
    }

    private async Task RefreshToolCatalogFromServiceAsync(ChatServiceClient client, bool publishOptions, bool appendWarnings) {
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var toolList = await client.RequestAsync<ToolListMessage>(
                new ListToolsRequest { RequestId = NextId() },
                cts.Token).ConfigureAwait(false);
            UpdateToolCatalog(toolList.Tools, toolList.RoutingCatalog, toolList.Packs, toolList.Plugins, toolList.CapabilitySnapshot);
            SeedBackgroundSchedulerSnapshot(toolList.CapabilitySnapshot?.BackgroundScheduler);
        } catch (Exception ex) {
            StartupLog.Write("RefreshToolCatalogFromServiceAsync failed: " + ex.GetType().Name + ": " + ex.Message);
            Debug.WriteLine("RefreshToolCatalogFromServiceAsync failed: " + ex);
            if (appendWarnings && (VerboseServiceLogs || _debugMode)) {
                AppendSystem(SystemNotice.ListToolsFailed(ex.Message));
            }
        }

        if (publishOptions) {
            await PublishOptionsStateAsync().ConfigureAwait(false);
        }
    }

    private async Task RefreshBackgroundSchedulerStatusAsync(
        ChatServiceClient client,
        bool publishOptions,
        bool appendWarnings,
        string? threadId = null,
        bool includeRecentActivity = false,
        bool includeThreadSummaries = true,
        int maxRecentActivity = 6,
        int maxThreadSummaries = 6) {
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var scopedRefresh = !string.IsNullOrWhiteSpace(threadId);
            var threadSampleLimit = ResolveBackgroundSchedulerThreadIdSampleLimit(includeThreadSummaries);
            var status = await client.GetBackgroundSchedulerStatusAsync(
                threadId: string.IsNullOrWhiteSpace(threadId) ? null : threadId.Trim(),
                includeRecentActivity: includeRecentActivity,
                includeThreadSummaries: includeThreadSummaries,
                maxReadyThreadIds: threadSampleLimit,
                maxRunningThreadIds: threadSampleLimit,
                maxRecentActivity: maxRecentActivity,
                maxThreadSummaries: ResolveBackgroundSchedulerThreadSummaryLimit(maxThreadSummaries),
                cancellationToken: cts.Token).ConfigureAwait(false);
            ApplyBackgroundSchedulerSnapshot(status.Scheduler, scopedRefresh);
        } catch (Exception ex) {
            RestoreBackgroundSchedulerSnapshotAfterRefreshFailure(!string.IsNullOrWhiteSpace(threadId));
            if (appendWarnings && (VerboseServiceLogs || _debugMode)) {
                AppendSystem("Couldn't load background scheduler status: " + ex.Message);
            }
        }

        if (publishOptions) {
            await PublishOptionsStateAsync().ConfigureAwait(false);
        }
    }

    internal static int ResolveBackgroundSchedulerThreadIdSampleLimit(bool includeThreadSummaries) {
        return includeThreadSummaries
            ? ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems
            : 8;
    }

    internal static int ResolveBackgroundSchedulerThreadSummaryLimit(int maxThreadSummaries) {
        return Math.Clamp(maxThreadSummaries, 0, ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems);
    }

    private async Task RefreshLocalRuntimeDetectionAsync(bool publishOptions) {
        var snapshot = await ProbeLocalRuntimeAvailabilityAsync().ConfigureAwait(false);
        ApplyLocalRuntimeDetectionSnapshot(snapshot);
        if (publishOptions) {
            await PublishOptionsStateAsync().ConfigureAwait(false);
        }
    }

    private async Task AutoDetectAndApplyLocalRuntimeAsync(bool forceModelRefresh) {
        var snapshot = await ProbeLocalRuntimeAvailabilityAsync().ConfigureAwait(false);
        ApplyLocalRuntimeDetectionSnapshot(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.DetectedBaseUrl)) {
            await SetStatusAsync("No local runtime detected. Start LM Studio or Ollama, then try again.").ConfigureAwait(false);
            await PublishOptionsStateAsync().ConfigureAwait(false);
            return;
        }

        await ApplyLocalProviderAsync(
            TransportCompatibleHttp,
            snapshot.DetectedBaseUrl,
            string.Empty,
            openAIAuthModeValue: _localProviderOpenAIAuthMode,
            openAIBasicUsernameValue: _localProviderOpenAIBasicUsername,
            openAIBasicPasswordValue: null,
            openAIAccountIdValue: _localProviderOpenAIAccountId,
            activeNativeAccountSlotValue: _activeNativeAccountSlot,
            activeSlotAccountIdValue: GetNativeAccountSlotId(_activeNativeAccountSlot),
            reasoningEffortValue: _localProviderReasoningEffort,
            reasoningSummaryValue: _localProviderReasoningSummary,
            textVerbosityValue: _localProviderTextVerbosity,
            temperatureValue: _localProviderTemperature?.ToString("0.###", CultureInfo.InvariantCulture),
            apiKeyValue: null,
            clearBasicAuth: false,
            clearApiKey: false,
            forceModelRefresh: forceModelRefresh,
            requestIdValue: null).ConfigureAwait(false);
    }

    private async Task RefreshServiceProfilesAsync(ChatServiceClient client, bool publishOptions, bool appendWarnings) {
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var profiles = await client.ListProfilesAsync(cts.Token).ConfigureAwait(false);
            _serviceProfileNames = NormalizeProfileNames(profiles.Profiles);
            _serviceActiveProfileName = string.IsNullOrWhiteSpace(profiles.ActiveProfile)
                ? null
                : profiles.ActiveProfile.Trim();
        } catch (Exception ex) {
            _serviceProfileNames = Array.Empty<string>();
            _serviceActiveProfileName = null;
            if (appendWarnings && (VerboseServiceLogs || _debugMode)) {
                AppendSystem("Couldn't load service profiles: " + ex.Message);
            }
        }

        if (publishOptions) {
            await PublishOptionsStateAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> TryApplyServiceProfileAsync(ChatServiceClient client, bool newThread, bool appendWarnings) {
        if (!ShouldApplyServiceProfile(_serviceProfileNames, _appProfileName, _serviceActiveProfileName, newThread)) {
            return false;
        }

        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            _ = await client.SetProfileAsync(_appProfileName, newThread, cts.Token).ConfigureAwait(false);
            _serviceActiveProfileName = _appProfileName;
            return true;
        } catch (Exception ex) {
            if (appendWarnings && (VerboseServiceLogs || _debugMode)) {
                AppendSystem("Couldn't apply service profile '" + _appProfileName + "': " + ex.Message);
            }
            return false;
        }
    }

    private async Task RefreshModelsAsync(ChatServiceClient client, bool forceRefresh, bool publishOptions, bool appendWarnings) {
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var modelList = await client.ListModelsAsync(forceRefresh, cts.Token).ConfigureAwait(false);
            _availableModels = NormalizeModelList(modelList.Models);
            _favoriteModels = NormalizeModelNames(modelList.FavoriteModels);
            _recentModels = NormalizeModelNames(modelList.RecentModels);
            _modelListIsStale = modelList.IsStale;
            _modelListWarning = string.IsNullOrWhiteSpace(modelList.Warning) ? null : modelList.Warning.Trim();
            var resolvedModel = ResolveChatRequestModelOverride(
                _localProviderTransport,
                _localProviderBaseUrl,
                _localProviderModel,
                _availableModels);
            _localProviderModel = (resolvedModel ?? string.Empty).Trim();
            _appState.LocalProviderModel = _localProviderModel;

            CaptureModelCatalogCacheIntoAppState();
            QueuePersistAppState();
        } catch (Exception ex) {
            _modelListIsStale = true;
            _modelListWarning = "Model discovery failed: " + ex.Message;
            CaptureModelCatalogCacheIntoAppState();
            if (appendWarnings && (VerboseServiceLogs || _debugMode)) {
                AppendSystem(_modelListWarning);
            }
        }

        if (publishOptions) {
            await PublishOptionsStateAsync().ConfigureAwait(false);
        }
    }

    private async Task ApplyLocalProviderAsync(string? transportValue, string? baseUrlValue, string? modelValue, string? openAIAuthModeValue,
        string? openAIBasicUsernameValue, string? openAIBasicPasswordValue, string? openAIAccountIdValue, int? activeNativeAccountSlotValue,
        string? activeSlotAccountIdValue, string? reasoningEffortValue, string? reasoningSummaryValue, string? textVerbosityValue,
        string? temperatureValue, string? apiKeyValue, bool clearBasicAuth, bool clearApiKey, bool forceModelRefresh, long? requestIdValue) {
        var normalizedRequestId = requestIdValue.GetValueOrDefault();
        if (normalizedRequestId <= 0) {
            normalizedRequestId = Interlocked.Increment(ref _runtimeApplyRequestCounter);
        } else {
            var currentCounter = Interlocked.Read(ref _runtimeApplyRequestCounter);
            if (normalizedRequestId > currentCounter) {
                Interlocked.Exchange(ref _runtimeApplyRequestCounter, normalizedRequestId);
            }
        }

        var request = new LocalProviderApplyRequest(
            Transport: transportValue,
            BaseUrl: baseUrlValue,
            Model: modelValue,
            OpenAIAuthMode: openAIAuthModeValue,
            OpenAIBasicUsername: openAIBasicUsernameValue,
            OpenAIBasicPassword: openAIBasicPasswordValue,
            OpenAIAccountId: openAIAccountIdValue,
            ActiveNativeAccountSlot: activeNativeAccountSlotValue,
            ActiveSlotAccountId: activeSlotAccountIdValue,
            ReasoningEffort: reasoningEffortValue,
            ReasoningSummary: reasoningSummaryValue,
            TextVerbosity: textVerbosityValue,
            Temperature: temperatureValue,
            ApiKey: apiKeyValue,
            ClearBasicAuth: clearBasicAuth,
            ClearApiKey: clearApiKey,
            ForceModelRefresh: forceModelRefresh,
            RequestId: normalizedRequestId);

        if (Interlocked.CompareExchange(ref _localProviderApplyInFlight, 1, 0) != 0) {
            QueuePendingLocalProviderApply(request);
            UpdateRuntimeApplyProgress("queued", "Runtime switch queued. Latest settings will apply next.", active: true, request.RequestId);
            await PublishOptionsStateAsync().ConfigureAwait(false);
            await SetStatusAsync("Runtime switch already in progress. Queued latest settings.").ConfigureAwait(false);
            return;
        }

        try {
            var current = request;
            var lastApplySucceeded = false;
            var anyAttempted = false;
            while (true) {
                anyAttempted = true;
                try {
                    lastApplySucceeded = await ApplyLocalProviderCoreAsync(current).ConfigureAwait(false);
                } catch (Exception ex) {
                    lastApplySucceeded = false;
                    var detail = "Runtime apply failed: " + ex.Message;
                    UpdateRuntimeApplyProgress("failed", detail, active: false, current.RequestId);
                    AppendSystem(detail);
                    await SetStatusAsync("Runtime apply failed. See System log.", SessionStatusTone.Warn).ConfigureAwait(false);
                    break;
                }

                if (!TryTakePendingLocalProviderApply(out var pending)) {
                    break;
                }

                if (pending == current) {
                    continue;
                }

                UpdateRuntimeApplyProgress("queued", "Applying queued runtime update...", active: true, pending.RequestId);
                current = pending;
            }

            if (anyAttempted && lastApplySucceeded) {
                UpdateRuntimeApplyProgress("completed", "Runtime settings applied without restarting the service process.", active: false, current.RequestId);
                await SetStatusAsync(ResolveConnectionStatusForCurrentTransport()).ConfigureAwait(false);
            }
        } finally {
            Interlocked.Exchange(ref _localProviderApplyInFlight, 0);
            await PublishOptionsStateAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> ApplyLocalProviderCoreAsync(LocalProviderApplyRequest request) {
        UpdateRuntimeApplyProgress("validating", "Validating runtime settings...", active: true, request.RequestId);
        await SetStatusAsync("Applying runtime settings...").ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);

        if (IsTurnDispatchInProgress()) {
            UpdateRuntimeApplyProgress("failed", "Finish the active response before changing runtime settings.", active: false, request.RequestId);
            await SetStatusAsync("Finish the active response before changing local runtime settings.").ConfigureAwait(false);
            return false;
        }

        var rawTransport = (request.Transport ?? string.Empty).Trim();
        string normalizedTransport;
        if (rawTransport.Length == 0) {
            normalizedTransport = NormalizeLocalProviderTransport(_localProviderTransport);
        } else if (!TryNormalizeLocalProviderTransport(rawTransport, out normalizedTransport)) {
            UpdateRuntimeApplyProgress("failed", "Invalid runtime transport value '" + rawTransport + "'.", active: false, request.RequestId);
            await SetStatusAsync("Invalid runtime transport value. Open Runtime settings and try again.").ConfigureAwait(false);
            await PublishOptionsStateAsync().ConfigureAwait(false);
            return false;
        }

        var normalizedBaseUrl = NormalizeLocalProviderBaseUrl(request.BaseUrl, normalizedTransport, rawTransport);
        var normalizedModel = NormalizeLocalProviderModel(request.Model, normalizedTransport);
        var normalizedOpenAIAuthMode = NormalizeLocalProviderOpenAIAuthMode(request.OpenAIAuthMode);
        var normalizedOpenAIBasicUsername = NormalizeLocalProviderOpenAIBasicUsername(request.OpenAIBasicUsername);
        var normalizedOpenAIBasicPassword = NormalizeLocalProviderOpenAIBasicPassword(request.OpenAIBasicPassword, normalizedTransport);
        var normalizedReasoningEffort = NormalizeLocalProviderReasoningEffort(request.ReasoningEffort);
        var normalizedReasoningSummary = NormalizeLocalProviderReasoningSummary(request.ReasoningSummary);
        var normalizedTextVerbosity = NormalizeLocalProviderTextVerbosity(request.TextVerbosity);
        var normalizedTemperature = NormalizeLocalProviderTemperature(request.Temperature);
        if (!SupportsLocalProviderReasoningControls(normalizedTransport, normalizedBaseUrl)) {
            normalizedReasoningEffort = string.Empty;
            normalizedReasoningSummary = string.Empty;
            normalizedTextVerbosity = string.Empty;
        }
        var clearApiKeyRequested = request.ClearApiKey && string.Equals(normalizedTransport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase);
        var clearBasicAuthRequested = request.ClearBasicAuth && string.Equals(normalizedTransport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase);
        var normalizedApiKey = NormalizeLocalProviderApiKey(request.ApiKey, normalizedTransport);
        var hasApiKeyUpdate = clearApiKeyRequested || normalizedApiKey is not null;
        var hasBasicPasswordUpdate = clearBasicAuthRequested || normalizedOpenAIBasicPassword is not null;
        var previousTransport = _localProviderTransport;
        var previousBaseUrl = _localProviderBaseUrl;
        var previousModel = _localProviderModel;
        var previousOpenAIAuthMode = _localProviderOpenAIAuthMode;
        var previousOpenAIBasicUsername = _localProviderOpenAIBasicUsername;
        var previousOpenAIAccountId = _localProviderOpenAIAccountId;
        var previousReasoningEffort = _localProviderReasoningEffort;
        var previousReasoningSummary = _localProviderReasoningSummary;
        var previousTextVerbosity = _localProviderTextVerbosity;
        var previousTemperature = _localProviderTemperature;
        var previousIsAuthenticated = _isAuthenticated;
        var previousAuthenticatedAccountId = _authenticatedAccountId;
        var previousLoginInProgress = _loginInProgress;
        var previousActiveNativeSlot = _activeNativeAccountSlot;
        var previousNativeSlots = SnapshotNativeAccountSlots();
        ApplyNativeAccountSlotSettings(request.ActiveNativeAccountSlot, request.ActiveSlotAccountId, request.OpenAIAccountId);
        var nativeAccountSlotsChanged = previousActiveNativeSlot != _activeNativeAccountSlot
                                        || HaveNativeAccountSlotsChanged(previousNativeSlots);
        if (clearBasicAuthRequested) {
            _localProviderOpenAIBasicUsername = string.Empty;
        } else if (request.OpenAIBasicUsername is not null) {
            _localProviderOpenAIBasicUsername = normalizedOpenAIBasicUsername;
        }

        var transportChanged = !string.Equals(previousTransport, normalizedTransport, StringComparison.OrdinalIgnoreCase);
        var changed = transportChanged
                      || !string.Equals(previousBaseUrl ?? string.Empty, normalizedBaseUrl ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                      || !string.Equals(previousModel, normalizedModel, StringComparison.Ordinal)
                      || !string.Equals(previousOpenAIAuthMode, normalizedOpenAIAuthMode, StringComparison.Ordinal)
                      || !string.Equals(previousOpenAIBasicUsername, _localProviderOpenAIBasicUsername, StringComparison.Ordinal)
                      || !string.Equals(previousOpenAIAccountId, _localProviderOpenAIAccountId, StringComparison.Ordinal)
                      || nativeAccountSlotsChanged
                      || !string.Equals(previousReasoningEffort, normalizedReasoningEffort, StringComparison.Ordinal)
                      || !string.Equals(previousReasoningSummary, normalizedReasoningSummary, StringComparison.Ordinal)
                      || !string.Equals(previousTextVerbosity, normalizedTextVerbosity, StringComparison.Ordinal)
                      || previousTemperature != normalizedTemperature
                      || hasBasicPasswordUpdate
                      || hasApiKeyUpdate;

        _localProviderTransport = normalizedTransport;
        _localProviderBaseUrl = normalizedBaseUrl;
        _localProviderModel = normalizedModel;
        _localProviderOpenAIAuthMode = normalizedOpenAIAuthMode;
        _localProviderReasoningEffort = normalizedReasoningEffort;
        _localProviderReasoningSummary = normalizedReasoningSummary;
        _localProviderTextVerbosity = normalizedTextVerbosity;
        _localProviderTemperature = normalizedTemperature;
        if (ShouldResetEnsureLoginProbeCacheForAuthContextChange(
                requiresInteractiveSignIn: RequiresInteractiveSignInForCurrentTransport()
                                           || string.Equals(previousTransport, TransportNative, StringComparison.OrdinalIgnoreCase),
                loginCompletedSuccessfully: false,
                transportChanged: transportChanged,
                runtimeExited: false)) {
            ResetEnsureLoginProbeCache();
        }
        if (!RequiresInteractiveSignInForCurrentTransport()) {
            ApplyNonNativeAuthenticationStateIfNeeded();
        } else if (transportChanged && !string.Equals(previousTransport, TransportNative, StringComparison.OrdinalIgnoreCase)) {
            SetInteractiveAuthenticationUnknown();
            _loginInProgress = false;
        }
        _appState.LocalProviderOpenAIAuthMode = _localProviderOpenAIAuthMode;
        _appState.LocalProviderOpenAIBasicUsername = _localProviderOpenAIBasicUsername;
        _appState.LocalProviderOpenAIAccountId = _localProviderOpenAIAccountId;
        _appState.LocalProviderTransport = _localProviderTransport;
        _appState.LocalProviderBaseUrl = _localProviderBaseUrl;
        _appState.LocalProviderModel = _localProviderModel;
        SyncNativeAccountSlotsToAppState();
        _appState.LocalProviderReasoningEffort = _localProviderReasoningEffort;
        _appState.LocalProviderReasoningSummary = _localProviderReasoningSummary;
        _appState.LocalProviderTextVerbosity = _localProviderTextVerbosity;
        _appState.LocalProviderTemperature = _localProviderTemperature;

        void RestorePreviousRuntimeState() {
            _localProviderTransport = previousTransport;
            _localProviderBaseUrl = previousBaseUrl;
            _localProviderModel = previousModel;
            _localProviderOpenAIAuthMode = previousOpenAIAuthMode;
            _localProviderOpenAIBasicUsername = previousOpenAIBasicUsername;
            _localProviderOpenAIAccountId = previousOpenAIAccountId;
            _localProviderReasoningEffort = previousReasoningEffort;
            _localProviderReasoningSummary = previousReasoningSummary;
            _localProviderTextVerbosity = previousTextVerbosity;
            _localProviderTemperature = previousTemperature;
            _activeNativeAccountSlot = previousActiveNativeSlot;
            RestoreNativeAccountSlotsFromSnapshot(previousNativeSlots);
            _isAuthenticated = previousIsAuthenticated;
            _authenticatedAccountId = previousAuthenticatedAccountId;
            _loginInProgress = previousLoginInProgress;

            _appState.LocalProviderOpenAIAuthMode = _localProviderOpenAIAuthMode;
            _appState.LocalProviderOpenAIBasicUsername = _localProviderOpenAIBasicUsername;
            _appState.LocalProviderOpenAIAccountId = _localProviderOpenAIAccountId;
            _appState.LocalProviderTransport = _localProviderTransport;
            _appState.LocalProviderBaseUrl = _localProviderBaseUrl;
            _appState.LocalProviderModel = _localProviderModel;
            SyncNativeAccountSlotsToAppState();
            _appState.LocalProviderReasoningEffort = _localProviderReasoningEffort;
            _appState.LocalProviderReasoningSummary = _localProviderReasoningSummary;
            _appState.LocalProviderTextVerbosity = _localProviderTextVerbosity;
            _appState.LocalProviderTemperature = _localProviderTemperature;
        }

        var profileSaved = ContainsProfileName(_serviceProfileNames, _appProfileName);
        UpdateRuntimeApplyProgress("persisting", "Saving runtime settings to the active profile...", active: true, request.RequestId);
        CaptureModelCatalogCacheIntoAppState();
        await PersistAppStateAsync().ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);

        if (!changed && profileSaved) {
            UpdateRuntimeApplyProgress("syncing", "Refreshing runtime metadata...", active: true, request.RequestId);
            await RefreshModelsFromUiAsync(request.ForceModelRefresh).ConfigureAwait(false);
            return true;
        }

        UpdateRuntimeApplyProgress("applying", "Applying runtime settings to the live session...", active: true, request.RequestId);
        var liveApply = await TryApplyRuntimeSettingsLiveAsync(
                // Always persist current runtime options for the active app profile so reconnects
                // can recover transport/model settings without falling back to defaults.
                profileSaved: true,
                model: _localProviderModel,
                openAITransport: _localProviderTransport,
                openAIBaseUrl: _localProviderBaseUrl,
                openAIAuthMode: _localProviderOpenAIAuthMode,
                openAIApiKey: normalizedApiKey,
                openAIBasicUsername: _localProviderOpenAIBasicUsername,
                openAIBasicPassword: normalizedOpenAIBasicPassword,
                openAIAccountId: _localProviderOpenAIAccountId,
                clearOpenAIBasicAuth: clearBasicAuthRequested,
                clearOpenAIApiKey: clearApiKeyRequested,
                openAIStreaming: true,
                openAIAllowInsecureHttp: ShouldAllowInsecureHttp(_localProviderTransport, _localProviderBaseUrl),
                reasoningEffort: _localProviderReasoningEffort,
                reasoningSummary: _localProviderReasoningSummary,
                textVerbosity: _localProviderTextVerbosity,
                temperature: _localProviderTemperature,
                enablePackIds: null,
                disablePackIds: null).ConfigureAwait(false);
        if (liveApply) {
            ClearConversationThreadIds();
            await PersistAppStateAsync().ConfigureAwait(false);
            UpdateRuntimeApplyProgress("syncing", "Refreshing runtime discovery and model catalog...", active: true, request.RequestId);
            await RefreshLocalRuntimeDetectionAsync(publishOptions: false).ConfigureAwait(false);
            await SyncConnectedServiceProfileAndModelsAsync(
                forceModelRefresh: true,
                setProfileNewThread: false,
                appendWarnings: true).ConfigureAwait(false);
            return true;
        }

        RestorePreviousRuntimeState();
        CaptureModelCatalogCacheIntoAppState();
        await PersistAppStateAsync().ConfigureAwait(false);
        UpdateRuntimeApplyProgress(
            "failed",
            "Runtime settings couldn't be applied live. Reverted to previously active runtime settings.",
            active: false,
            request.RequestId);
        AppendSystem("Runtime settings couldn't be applied live. Reverted to the previous runtime settings.");
        await SetStatusAsync("Runtime settings couldn't be applied live. Reverted to previous settings.", SessionStatusTone.Warn)
            .ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);
        return false;
    }

    private async Task<bool> TryApplyRuntimeSettingsLiveAsync(
        bool profileSaved,
        string? model,
        string? openAITransport,
        string? openAIBaseUrl,
        string? openAIAuthMode,
        string? openAIApiKey,
        string? openAIBasicUsername,
        string? openAIBasicPassword,
        string? openAIAccountId,
        bool clearOpenAIBasicAuth,
        bool clearOpenAIApiKey,
        bool? openAIStreaming,
        bool? openAIAllowInsecureHttp,
        string? reasoningEffort,
        string? reasoningSummary,
        string? textVerbosity,
        double? temperature,
        IReadOnlyList<string>? enablePackIds,
        IReadOnlyList<string>? disablePackIds) {
        var client = _client;
        if (client is null || !await IsClientAliveAsync(client).ConfigureAwait(false)) {
            if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
                return false;
            }

            client = _client;
            if (client is null || !await IsClientAliveAsync(client).ConfigureAwait(false)) {
                return false;
            }
        }

        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            _ = await client.ApplyRuntimeSettingsAsync(
                    model: model,
                    openAITransport: openAITransport,
                    openAIBaseUrl: openAIBaseUrl,
                    openAIAuthMode: openAIAuthMode,
                    openAIApiKey: openAIApiKey,
                    openAIBasicUsername: openAIBasicUsername,
                    openAIBasicPassword: openAIBasicPassword,
                    openAIAccountId: openAIAccountId,
                    clearOpenAIBasicAuth: clearOpenAIBasicAuth,
                    clearOpenAIApiKey: clearOpenAIApiKey,
                    openAIStreaming: openAIStreaming,
                    openAIAllowInsecureHttp: openAIAllowInsecureHttp,
                    reasoningEffort: reasoningEffort,
                    reasoningSummary: reasoningSummary,
                    textVerbosity: textVerbosity,
                    temperature: temperature,
                    enablePackIds: enablePackIds,
                    disablePackIds: disablePackIds,
                    profileName: profileSaved ? _appProfileName : null,
                    cancellationToken: cts.Token)
                .ConfigureAwait(false);
            return true;
        } catch (Exception ex) {
            AppendSystem("Live runtime apply failed (session kept running). " + ex.Message);
            return false;
        }
    }

    private void QueuePendingLocalProviderApply(LocalProviderApplyRequest request) {
        lock (_localProviderApplySync) {
            _pendingLocalProviderApply = request;
        }
    }

    private bool TryTakePendingLocalProviderApply(out LocalProviderApplyRequest request) {
        lock (_localProviderApplySync) {
            if (_pendingLocalProviderApply is null) {
                request = new LocalProviderApplyRequest(
                    Transport: null,
                    BaseUrl: null,
                    Model: null,
                    OpenAIAuthMode: null,
                    OpenAIBasicUsername: null,
                    OpenAIBasicPassword: null,
                    OpenAIAccountId: null,
                    ActiveNativeAccountSlot: null,
                    ActiveSlotAccountId: null,
                    ReasoningEffort: null,
                    ReasoningSummary: null,
                    TextVerbosity: null,
                    Temperature: null,
                    ApiKey: null,
                    ClearBasicAuth: false,
                    ClearApiKey: false,
                    ForceModelRefresh: false,
                    RequestId: 0);
                return false;
            }

            request = _pendingLocalProviderApply;
            _pendingLocalProviderApply = null;
            return true;
        }
    }

    private async Task ReconnectServiceSessionAsync() {
        QueueServiceLaunchProfileSyncSnapshot();
        StopAutoReconnectLoop();
        await DisposeClientAsync().ConfigureAwait(false);
        await ConnectAsync(fromUserAction: false, connectBudgetOverride: DispatchConnectBudget).ConfigureAwait(false);
    }

    private void QueueServiceLaunchProfileSyncSnapshot() {
        _pendingServiceLaunchProfileOptions = CaptureCurrentServiceLaunchProfileOptions();
    }

    private ServiceLaunchProfileOptions CaptureCurrentServiceLaunchProfileOptions() {
        var profileName = (_appProfileName ?? string.Empty).Trim();
        if (profileName.Length == 0) {
            profileName = ResolveAppProfileName("default");
        }

        var model = (_localProviderModel ?? string.Empty).Trim();
        var transport = (_localProviderTransport ?? string.Empty).Trim();
        var baseUrl = (_localProviderBaseUrl ?? string.Empty).Trim();
        var authMode = (_localProviderOpenAIAuthMode ?? string.Empty).Trim();
        var basicUsername = (_localProviderOpenAIBasicUsername ?? string.Empty).Trim();
        var accountId = (_localProviderOpenAIAccountId ?? string.Empty).Trim();
        var reasoningEffort = (_localProviderReasoningEffort ?? string.Empty).Trim();
        var reasoningSummary = (_localProviderReasoningSummary ?? string.Empty).Trim();
        var textVerbosity = (_localProviderTextVerbosity ?? string.Empty).Trim();

        return new ServiceLaunchProfileOptions {
            LoadProfileName = profileName,
            SaveProfileName = profileName,
            Model = model.Length == 0 ? null : model,
            OpenAITransport = transport.Length == 0 ? null : transport,
            OpenAIBaseUrl = baseUrl.Length == 0 ? null : baseUrl,
            OpenAIAuthMode = authMode.Length == 0 ? null : authMode,
            OpenAIBasicUsername = basicUsername.Length == 0 ? null : basicUsername,
            OpenAIAccountId = accountId.Length == 0 ? null : accountId,
            OpenAIStreaming = true,
            OpenAIAllowInsecureHttp = ShouldAllowInsecureHttp(transport, baseUrl.Length == 0 ? null : baseUrl),
            ReasoningEffort = reasoningEffort.Length == 0 ? null : reasoningEffort,
            ReasoningSummary = reasoningSummary.Length == 0 ? null : reasoningSummary,
            TextVerbosity = textVerbosity.Length == 0 ? null : textVerbosity,
            Temperature = _localProviderTemperature,
            PackToggles = BuildRuntimePackTogglesFromSessionPolicy()
        };
    }

    private ServiceLaunchArguments.PackToggle[]? BuildRuntimePackTogglesFromSessionPolicy() {
        var packs = RuntimeToolingMetadataResolver.ResolveEffectivePacks(
            _sessionPolicy,
            _toolCatalogPacks,
            _toolCatalogCapabilitySnapshot);
        if (packs.Length == 0) {
            return null;
        }

        var togglesById = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < packs.Length; i++) {
            var pack = packs[i];
            var normalizedPackId = NormalizeRuntimePackId(pack.Id);
            if (normalizedPackId.Length == 0) {
                continue;
            }

            togglesById[normalizedPackId] = pack.Enabled;
        }

        if (togglesById.Count == 0) {
            return null;
        }

        var ids = new List<string>(togglesById.Keys);
        ids.Sort(StringComparer.OrdinalIgnoreCase);
        var toggles = new ServiceLaunchArguments.PackToggle[ids.Count];
        for (var i = 0; i < ids.Count; i++) {
            var packId = ids[i];
            toggles[i] = new ServiceLaunchArguments.PackToggle(packId, togglesById[packId]);
        }

        return toggles;
    }

    private void BuildRuntimePackToggleLists(out string[]? enablePackIds, out string[]? disablePackIds) {
        enablePackIds = null;
        disablePackIds = null;
        var packToggles = BuildRuntimePackTogglesFromSessionPolicy();
        if (packToggles is null || packToggles.Length == 0) {
            return;
        }

        var enableList = new List<string>();
        var disableList = new List<string>();
        for (var i = 0; i < packToggles.Length; i++) {
            if (packToggles[i].Enabled) {
                enableList.Add(packToggles[i].PackId);
            } else {
                disableList.Add(packToggles[i].PackId);
            }
        }

        enablePackIds = enableList.Count == 0 ? null : enableList.ToArray();
        disablePackIds = disableList.Count == 0 ? null : disableList.ToArray();
    }

    private void ClearConversationThreadIds() {
        for (var i = 0; i < _conversations.Count; i++) {
            _conversations[i].ThreadId = null;
        }

        _threadId = null;
        _appState.ThreadId = null;
    }

}
