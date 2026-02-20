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

    private void UpdateRuntimeApplyProgress(string stage, string? detail, bool active) {
        var normalizedStage = (stage ?? string.Empty).Trim().ToLowerInvariant();
        _runtimeApplyStage = normalizedStage.Length == 0 ? "idle" : normalizedStage;
        _runtimeApplyDetail = (detail ?? string.Empty).Trim();
        _runtimeApplyActive = active;
        _runtimeApplyUpdatedUtc = DateTime.UtcNow;
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
        await PublishOptionsStateAsync().ConfigureAwait(false);
        return profileApplied;
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
            forceModelRefresh: forceModelRefresh).ConfigureAwait(false);
    }

    private async Task RefreshServiceProfilesAsync(ChatServiceClient client, bool publishOptions, bool appendWarnings) {
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var profiles = await client.ListProfilesAsync(cts.Token).ConfigureAwait(false);
            _serviceProfileNames = NormalizeProfileNames(profiles.Profiles);
        } catch (Exception ex) {
            _serviceProfileNames = Array.Empty<string>();
            if (appendWarnings && (VerboseServiceLogs || _debugMode)) {
                AppendSystem("Couldn't load service profiles: " + ex.Message);
            }
        }

        if (publishOptions) {
            await PublishOptionsStateAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> TryApplyServiceProfileAsync(ChatServiceClient client, bool newThread, bool appendWarnings) {
        if (!ContainsProfileName(_serviceProfileNames, _appProfileName)) {
            return false;
        }

        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            _ = await client.SetProfileAsync(_appProfileName, newThread, cts.Token).ConfigureAwait(false);
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
        string? temperatureValue, string? apiKeyValue, bool clearBasicAuth, bool clearApiKey, bool forceModelRefresh) {
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
            ForceModelRefresh: forceModelRefresh);

        if (Interlocked.CompareExchange(ref _localProviderApplyInFlight, 1, 0) != 0) {
            QueuePendingLocalProviderApply(request);
            UpdateRuntimeApplyProgress("queued", "Runtime switch queued. Latest settings will apply next.", active: true);
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
                    UpdateRuntimeApplyProgress("failed", "Runtime apply failed: " + ex.Message, active: false);
                    await SetStatusAsync("Runtime apply failed. " + ex.Message).ConfigureAwait(false);
                    break;
                }

                if (!TryTakePendingLocalProviderApply(out var pending)) {
                    break;
                }

                if (pending == current) {
                    continue;
                }

                UpdateRuntimeApplyProgress("queued", "Applying queued runtime update...", active: true);
                current = pending;
            }

            if (anyAttempted && lastApplySucceeded) {
                UpdateRuntimeApplyProgress("completed", "Runtime settings applied without restarting the service process.", active: false);
            }
        } finally {
            Interlocked.Exchange(ref _localProviderApplyInFlight, 0);
            await PublishOptionsStateAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> ApplyLocalProviderCoreAsync(LocalProviderApplyRequest request) {
        UpdateRuntimeApplyProgress("validating", "Validating runtime settings...", active: true);
        await SetStatusAsync("Applying runtime settings...").ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);

        if (_isSending) {
            UpdateRuntimeApplyProgress("failed", "Finish the active response before changing runtime settings.", active: false);
            await SetStatusAsync("Finish the active response before changing local runtime settings.").ConfigureAwait(false);
            return false;
        }

        var rawTransport = (request.Transport ?? string.Empty).Trim();
        string normalizedTransport;
        if (rawTransport.Length == 0) {
            normalizedTransport = NormalizeLocalProviderTransport(_localProviderTransport);
        } else if (!TryNormalizeLocalProviderTransport(rawTransport, out normalizedTransport)) {
            UpdateRuntimeApplyProgress("failed", "Invalid runtime transport value '" + rawTransport + "'.", active: false);
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
        var previousOpenAIAuthMode = _localProviderOpenAIAuthMode;
        var previousOpenAIBasicUsername = _localProviderOpenAIBasicUsername;
        var previousOpenAIAccountId = _localProviderOpenAIAccountId;
        var previousActiveNativeSlot = _activeNativeAccountSlot;
        var previousNativeSlot1 = _nativeAccountSlots[0];
        var previousNativeSlot2 = _nativeAccountSlots[1];
        var previousNativeSlot3 = _nativeAccountSlots[2];
        ApplyNativeAccountSlotSettings(request.ActiveNativeAccountSlot, request.ActiveSlotAccountId, request.OpenAIAccountId);
        var nativeAccountSlotsChanged = previousActiveNativeSlot != _activeNativeAccountSlot
                                        || !string.Equals(previousNativeSlot1, _nativeAccountSlots[0], StringComparison.Ordinal)
                                        || !string.Equals(previousNativeSlot2, _nativeAccountSlots[1], StringComparison.Ordinal)
                                        || !string.Equals(previousNativeSlot3, _nativeAccountSlots[2], StringComparison.Ordinal);
        if (clearBasicAuthRequested) {
            _localProviderOpenAIBasicUsername = string.Empty;
        } else if (request.OpenAIBasicUsername is not null) {
            _localProviderOpenAIBasicUsername = normalizedOpenAIBasicUsername;
        }

        var transportChanged = !string.Equals(previousTransport, normalizedTransport, StringComparison.OrdinalIgnoreCase);
        var changed = transportChanged
                      || !string.Equals(_localProviderBaseUrl ?? string.Empty, normalizedBaseUrl ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                      || !string.Equals(_localProviderModel, normalizedModel, StringComparison.Ordinal)
                      || !string.Equals(previousOpenAIAuthMode, normalizedOpenAIAuthMode, StringComparison.Ordinal)
                      || !string.Equals(previousOpenAIBasicUsername, _localProviderOpenAIBasicUsername, StringComparison.Ordinal)
                      || !string.Equals(previousOpenAIAccountId, _localProviderOpenAIAccountId, StringComparison.Ordinal)
                      || nativeAccountSlotsChanged
                      || !string.Equals(_localProviderReasoningEffort, normalizedReasoningEffort, StringComparison.Ordinal)
                      || !string.Equals(_localProviderReasoningSummary, normalizedReasoningSummary, StringComparison.Ordinal)
                      || !string.Equals(_localProviderTextVerbosity, normalizedTextVerbosity, StringComparison.Ordinal)
                      || _localProviderTemperature != normalizedTemperature
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
        if (!RequiresInteractiveSignInForCurrentTransport()) {
            ApplyNonNativeAuthenticationStateIfNeeded();
        } else if (transportChanged && !string.Equals(previousTransport, TransportNative, StringComparison.OrdinalIgnoreCase)) {
            _isAuthenticated = false;
            _authenticatedAccountId = null;
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

        var profileSaved = ContainsProfileName(_serviceProfileNames, _appProfileName);
        UpdateRuntimeApplyProgress("persisting", "Saving runtime settings to the active profile...", active: true);
        CaptureModelCatalogCacheIntoAppState();
        await PersistAppStateAsync().ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);

        if (!changed && profileSaved) {
            UpdateRuntimeApplyProgress("syncing", "Refreshing runtime metadata...", active: true);
            await RefreshModelsFromUiAsync(request.ForceModelRefresh).ConfigureAwait(false);
            return true;
        }

        ClearConversationThreadIds();
        await PersistAppStateAsync().ConfigureAwait(false);

        UpdateRuntimeApplyProgress("applying", "Applying runtime settings to the live session...", active: true);
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
                enablePowerShellPack: null,
                enableTestimoXPack: null,
                enableOfficeImoPack: null).ConfigureAwait(false);
        if (liveApply) {
            UpdateRuntimeApplyProgress("syncing", "Refreshing runtime discovery and model catalog...", active: true);
            await RefreshLocalRuntimeDetectionAsync(publishOptions: false).ConfigureAwait(false);
            await SyncConnectedServiceProfileAndModelsAsync(
                forceModelRefresh: true,
                setProfileNewThread: false,
                appendWarnings: true).ConfigureAwait(false);
            return true;
        }

        UpdateRuntimeApplyProgress(
            "failed",
            "Runtime settings couldn't be applied live. Session stayed running; review credentials/settings and apply again.",
            active: false);
        await SetStatusAsync(
                "Runtime settings couldn't be applied live. Session stayed running; verify credentials/settings and apply again.")
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
        bool? enablePowerShellPack,
        bool? enableTestimoXPack,
        bool? enableOfficeImoPack) {
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
                    enablePowerShellPack: enablePowerShellPack,
                    enableTestimoXPack: enableTestimoXPack,
                    enableOfficeImoPack: enableOfficeImoPack,
                    profileName: profileSaved ? _appProfileName : null,
                    cancellationToken: cts.Token)
                .ConfigureAwait(false);
            return true;
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                AppendSystem("Live runtime apply failed (session kept running). " + ex.Message);
            }
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
                    ForceModelRefresh: false);
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
        await ConnectAsync(fromUserAction: false).ConfigureAwait(false);
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
            EnablePowerShellPack = ResolveRuntimeManagedPackEnabled("powershell"),
            EnableTestimoXPack = ResolveRuntimeManagedPackEnabled("testimox"),
            EnableOfficeImoPack = ResolveRuntimeManagedPackEnabled("officeimo")
        };
    }

    private bool? ResolveRuntimeManagedPackEnabled(string packId) {
        if (_sessionPolicy?.Packs is not { Length: > 0 }) {
            return null;
        }

        var normalizedPackId = (packId ?? string.Empty).Trim();
        if (normalizedPackId.Length == 0) {
            return null;
        }

        for (var i = 0; i < _sessionPolicy.Packs.Length; i++) {
            var pack = _sessionPolicy.Packs[i];
            if (string.Equals(pack.Id, normalizedPackId, StringComparison.OrdinalIgnoreCase)) {
                return pack.Enabled;
            }
        }

        return null;
    }

    private void ClearConversationThreadIds() {
        for (var i = 0; i < _conversations.Count; i++) {
            _conversations[i].ThreadId = null;
        }

        _threadId = null;
        _appState.ThreadId = null;
    }

    private static string[] NormalizeProfileNames(string[]? names) {
        if (names is null || names.Length == 0) {
            return Array.Empty<string>();
        }

        var list = new List<string>(names.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < names.Length; i++) {
            var normalized = (names[i] ?? string.Empty).Trim();
            if (normalized.Length == 0 || !seen.Add(normalized)) {
                continue;
            }

            list.Add(normalized);
        }

        if (list.Count == 0) {
            return Array.Empty<string>();
        }

        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list.ToArray();
    }

    private static string[] NormalizeModelNames(string[]? names) {
        if (names is null || names.Length == 0) {
            return Array.Empty<string>();
        }

        var list = new List<string>(names.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < names.Length; i++) {
            var normalized = (names[i] ?? string.Empty).Trim();
            if (normalized.Length == 0 || !seen.Add(normalized)) {
                continue;
            }

            list.Add(normalized);
        }

        return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
    }

    private static ModelInfoDto[] NormalizeModelList(ModelInfoDto[]? models) {
        if (models is null || models.Length == 0) {
            return Array.Empty<ModelInfoDto>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<ModelInfoDto>(models.Length);
        for (var i = 0; i < models.Length; i++) {
            var entry = models[i];
            if (entry is null) {
                continue;
            }

            var model = (entry.Model ?? string.Empty).Trim();
            if (model.Length == 0 || !seen.Add(model)) {
                continue;
            }

            var id = (entry.Id ?? string.Empty).Trim();
            if (id.Length == 0) {
                id = model;
            }

            list.Add(new ModelInfoDto {
                Id = id,
                Model = model,
                DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? null : entry.DisplayName.Trim(),
                Description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description.Trim(),
                IsDefault = entry.IsDefault,
                OwnedBy = string.IsNullOrWhiteSpace(entry.OwnedBy) ? null : entry.OwnedBy.Trim(),
                Publisher = string.IsNullOrWhiteSpace(entry.Publisher) ? null : entry.Publisher.Trim(),
                Architecture = string.IsNullOrWhiteSpace(entry.Architecture) ? null : entry.Architecture.Trim(),
                Quantization = string.IsNullOrWhiteSpace(entry.Quantization) ? null : entry.Quantization.Trim(),
                CompatibilityType = string.IsNullOrWhiteSpace(entry.CompatibilityType) ? null : entry.CompatibilityType.Trim(),
                RuntimeState = string.IsNullOrWhiteSpace(entry.RuntimeState) ? null : entry.RuntimeState.Trim(),
                ModelType = string.IsNullOrWhiteSpace(entry.ModelType) ? null : entry.ModelType.Trim(),
                MaxContextLength = entry.MaxContextLength,
                LoadedContextLength = entry.LoadedContextLength,
                Capabilities = NormalizeModelNames(entry.Capabilities),
                DefaultReasoningEffort = string.IsNullOrWhiteSpace(entry.DefaultReasoningEffort) ? null : entry.DefaultReasoningEffort.Trim(),
                SupportedReasoningEfforts = entry.SupportedReasoningEfforts ?? Array.Empty<ReasoningEffortOptionDto>()
            });
        }

        return list.Count == 0 ? Array.Empty<ModelInfoDto>() : list.ToArray();
    }

    private static bool ContainsProfileName(string[] names, string profileName) {
        if (names.Length == 0 || string.IsNullOrWhiteSpace(profileName)) {
            return false;
        }

        for (var i = 0; i < names.Length; i++) {
            if (string.Equals(names[i], profileName, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsModel(ModelInfoDto[] models, string modelName) {
        if (models is null || models.Length == 0 || string.IsNullOrWhiteSpace(modelName)) {
            return false;
        }

        for (var i = 0; i < models.Length; i++) {
            if (string.Equals(models[i].Model, modelName, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private void RestoreCachedModelCatalogFromAppState() {
        var cacheTransport = NormalizeLocalProviderTransport(_appState.CachedModelsTransport);
        var cacheBaseUrl = NormalizeLocalProviderBaseUrl(_appState.CachedModelsBaseUrl, cacheTransport, cacheTransport);
        var sameRuntime = string.Equals(cacheTransport, _localProviderTransport, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(cacheBaseUrl ?? string.Empty, _localProviderBaseUrl ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        if (!sameRuntime) {
            _availableModels = Array.Empty<ModelInfoDto>();
            _favoriteModels = Array.Empty<string>();
            _recentModels = Array.Empty<string>();
            _modelListIsStale = false;
            _modelListWarning = null;
            return;
        }

        _availableModels = _appState.CachedModels is { Count: > 0 }
            ? NormalizeModelList(_appState.CachedModels.ToArray())
            : Array.Empty<ModelInfoDto>();
        _favoriteModels = _appState.CachedFavoriteModels is { Count: > 0 }
            ? NormalizeModelNames(_appState.CachedFavoriteModels.ToArray())
            : Array.Empty<string>();
        _recentModels = _appState.CachedRecentModels is { Count: > 0 }
            ? NormalizeModelNames(_appState.CachedRecentModels.ToArray())
            : Array.Empty<string>();
        _modelListIsStale = _availableModels.Length > 0 || _appState.CachedModelListIsStale;
        _modelListWarning = string.IsNullOrWhiteSpace(_appState.CachedModelListWarning)
            ? (_availableModels.Length > 0 ? "Showing cached models while runtime connects." : null)
            : _appState.CachedModelListWarning.Trim();
    }

    private void CaptureModelCatalogCacheIntoAppState() {
        _appState.CachedModelsTransport = _localProviderTransport;
        _appState.CachedModelsBaseUrl = _localProviderBaseUrl;
        _appState.CachedModels = CloneModelList(_availableModels);
        _appState.CachedFavoriteModels = new List<string>(_favoriteModels);
        _appState.CachedRecentModels = new List<string>(_recentModels);
        _appState.CachedModelListIsStale = _modelListIsStale;
        _appState.CachedModelListWarning = _modelListWarning;
        _appState.CachedModelsUpdatedUtc = DateTime.UtcNow;
    }

    private static List<ModelInfoDto> CloneModelList(ModelInfoDto[] models) {
        if (models is null || models.Length == 0) {
            return new List<ModelInfoDto>();
        }

        const int maxCachedModels = 250;
        var limit = Math.Min(models.Length, maxCachedModels);
        var clone = new List<ModelInfoDto>(limit);
        for (var i = 0; i < limit; i++) {
            var model = models[i];
            if (model is null || string.IsNullOrWhiteSpace(model.Model)) {
                continue;
            }

            clone.Add(new ModelInfoDto {
                Id = model.Id,
                Model = model.Model,
                DisplayName = model.DisplayName,
                Description = model.Description,
                IsDefault = model.IsDefault,
                OwnedBy = model.OwnedBy,
                Publisher = model.Publisher,
                Architecture = model.Architecture,
                Quantization = model.Quantization,
                CompatibilityType = model.CompatibilityType,
                RuntimeState = model.RuntimeState,
                ModelType = model.ModelType,
                MaxContextLength = model.MaxContextLength,
                LoadedContextLength = model.LoadedContextLength,
                Capabilities = model.Capabilities is { Length: > 0 } ? NormalizeModelNames(model.Capabilities) : Array.Empty<string>(),
                DefaultReasoningEffort = model.DefaultReasoningEffort,
                SupportedReasoningEfforts = model.SupportedReasoningEfforts is { Length: > 0 }
                    ? model.SupportedReasoningEfforts
                    : Array.Empty<ReasoningEffortOptionDto>()
            });
        }

        return clone;
    }

    private static bool ShouldAllowInsecureHttp(string transport, string? baseUrl) {
        if (!string.Equals(transport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var value = (baseUrl ?? string.Empty).Trim();
        if (value.Length == 0) {
            return true;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<LocalRuntimeDetectionSnapshot> ProbeLocalRuntimeAvailabilityAsync() {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var lmStudio = await ProbeModelsEndpointAsync(DefaultLmStudioBaseUrl, cts.Token).ConfigureAwait(false);
        var ollama = await ProbeModelsEndpointAsync(DefaultOllamaBaseUrl, cts.Token).ConfigureAwait(false);

        if (lmStudio == ModelsProbeAvailability.Available) {
            return new LocalRuntimeDetectionSnapshot(
                LmStudioAvailable: true,
                OllamaAvailable: ollama == ModelsProbeAvailability.Available,
                DetectedName: "LM Studio",
                DetectedBaseUrl: DefaultLmStudioBaseUrl,
                Warning: null);
        }

        if (ollama == ModelsProbeAvailability.Available) {
            return new LocalRuntimeDetectionSnapshot(
                LmStudioAvailable: false,
                OllamaAvailable: true,
                DetectedName: "Ollama",
                DetectedBaseUrl: DefaultOllamaBaseUrl,
                Warning: null);
        }

        // If localhost runtimes are unavailable, still probe the currently configured compatible-http endpoint
        // (for example external LM Studio, Azure OpenAI, or another OpenAI-compatible provider).
        if (string.Equals(_localProviderTransport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)) {
            var configuredBaseUrl = (_localProviderBaseUrl ?? string.Empty).Trim();
            if (configuredBaseUrl.Length > 0
                && !string.Equals(configuredBaseUrl, DefaultLmStudioBaseUrl, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(configuredBaseUrl, DefaultOllamaBaseUrl, StringComparison.OrdinalIgnoreCase)) {
                var configuredAvailability = await ProbeModelsEndpointAsync(configuredBaseUrl, cts.Token).ConfigureAwait(false);
                if (IsConfiguredCompatibleEndpointDetected(configuredAvailability)) {
                    return new LocalRuntimeDetectionSnapshot(
                        LmStudioAvailable: false,
                        OllamaAvailable: false,
                        DetectedName: DescribeRuntimeFromBaseUrl(configuredBaseUrl),
                        DetectedBaseUrl: configuredBaseUrl,
                        Warning: null);
                }
            }
        }

        return new LocalRuntimeDetectionSnapshot(
            LmStudioAvailable: false,
            OllamaAvailable: false,
            DetectedName: null,
            DetectedBaseUrl: null,
            Warning: "No local runtime detected on localhost ports 1234 or 11434.");
    }

    private void ApplyLocalRuntimeDetectionSnapshot(LocalRuntimeDetectionSnapshot snapshot) {
        _localRuntimeDetectionRan = true;
        _localRuntimeLmStudioAvailable = snapshot.LmStudioAvailable;
        _localRuntimeOllamaAvailable = snapshot.OllamaAvailable;
        _localRuntimeDetectedName = snapshot.DetectedName;
        _localRuntimeDetectedBaseUrl = snapshot.DetectedBaseUrl;
        _localRuntimeDetectionWarning = snapshot.Warning;
    }

    internal static bool IsConfiguredCompatibleEndpointDetected(ModelsProbeAvailability availability) {
        return availability == ModelsProbeAvailability.Available
               || availability == ModelsProbeAvailability.ReachableAuthRequired;
    }

    private static async Task<ModelsProbeAvailability> ProbeModelsEndpointAsync(string baseUrl, CancellationToken cancellationToken) {
        var probeUrl = BuildModelsProbeUrl(baseUrl);
        using var handler = new HttpClientHandler {
            UseProxy = false
        };
        using var client = new HttpClient(handler) {
            Timeout = TimeSpan.FromSeconds(2)
        };

        try {
            using var request = new HttpRequestMessage(HttpMethod.Get, probeUrl);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            return ClassifyModelsProbeResponse(response.StatusCode);
        } catch {
            return ModelsProbeAvailability.Unavailable;
        }
    }

    internal static ModelsProbeAvailability ClassifyModelsProbeResponse(HttpStatusCode statusCode) {
        if ((int)statusCode >= 200 && (int)statusCode <= 299) {
            return ModelsProbeAvailability.Available;
        }

        if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden) {
            return ModelsProbeAvailability.ReachableAuthRequired;
        }

        return ModelsProbeAvailability.Unavailable;
    }

    private static string BuildModelsProbeUrl(string baseUrl) {
        var normalized = (baseUrl ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return "http://127.0.0.1:11434/v1/models";
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)) {
            return normalized;
        }

        var path = uri.AbsolutePath ?? string.Empty;
        if (!path.EndsWith("/", StringComparison.Ordinal)) {
            path += "/";
        }

        if (!path.Contains("/v1/", StringComparison.OrdinalIgnoreCase)) {
            path += "v1/";
        }

        var builder = new UriBuilder(uri) {
            Path = path + "models",
            Query = string.Empty
        };

        return builder.Uri.ToString();
    }

    private static string DescribeRuntimeFromBaseUrl(string baseUrl) {
        var normalized = (baseUrl ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return "Compatible HTTP runtime";
        }

        if (normalized.Contains("api.githubcopilot.com", StringComparison.OrdinalIgnoreCase)) {
            return "GitHub Copilot endpoint";
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)) {
            return uri.Host;
        }

        return "Compatible HTTP runtime";
    }
}
