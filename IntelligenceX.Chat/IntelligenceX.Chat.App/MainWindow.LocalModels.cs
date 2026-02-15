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

            if (string.IsNullOrWhiteSpace(_localProviderModel) && _availableModels.Length > 0) {
                _localProviderModel = _availableModels[0].Model;
            }
        } catch (Exception ex) {
            _modelListIsStale = true;
            _modelListWarning = "Model discovery failed: " + ex.Message;
            if (appendWarnings && (VerboseServiceLogs || _debugMode)) {
                AppendSystem(_modelListWarning);
            }
        }

        if (publishOptions) {
            await PublishOptionsStateAsync().ConfigureAwait(false);
        }
    }

    private async Task ApplyLocalProviderAsync(string? transportValue, string? baseUrlValue, string? modelValue, bool forceModelRefresh) {
        if (_isSending) {
            await SetStatusAsync("Finish the active response before changing local runtime settings.").ConfigureAwait(false);
            return;
        }

        var rawTransport = (transportValue ?? string.Empty).Trim();
        var normalizedTransport = NormalizeLocalProviderTransport(rawTransport);
        var normalizedBaseUrl = NormalizeLocalProviderBaseUrl(baseUrlValue, normalizedTransport, rawTransport);
        var normalizedModel = NormalizeLocalProviderModel(modelValue);

        var changed = !string.Equals(_localProviderTransport, normalizedTransport, StringComparison.OrdinalIgnoreCase)
                      || !string.Equals(_localProviderBaseUrl ?? string.Empty, normalizedBaseUrl ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                      || !string.Equals(_localProviderModel, normalizedModel, StringComparison.Ordinal);

        _localProviderTransport = normalizedTransport;
        _localProviderBaseUrl = normalizedBaseUrl;
        _localProviderModel = normalizedModel;
        _appState.LocalProviderTransport = _localProviderTransport;
        _appState.LocalProviderBaseUrl = _localProviderBaseUrl;
        _appState.LocalProviderModel = _localProviderModel;

        var profileSaved = ContainsProfileName(_serviceProfileNames, _appProfileName);
        await PersistAppStateAsync().ConfigureAwait(false);

        if (!changed && profileSaved) {
            await RefreshModelsFromUiAsync(forceModelRefresh).ConfigureAwait(false);
            return;
        }

        ClearConversationThreadIds();
        await PersistAppStateAsync().ConfigureAwait(false);

        _pendingServiceLaunchProfileOptions = new ServiceLaunchProfileOptions {
            LoadProfileName = _appProfileName,
            SaveProfileName = _appProfileName,
            Model = _localProviderModel,
            OpenAITransport = _localProviderTransport,
            OpenAIBaseUrl = _localProviderBaseUrl,
            OpenAIApiKey = null,
            OpenAIStreaming = true,
            OpenAIAllowInsecureHttp = ShouldAllowInsecureHttp(_localProviderTransport, _localProviderBaseUrl)
        };

        await RestartSidecarAsync().ConfigureAwait(false);
        await SyncConnectedServiceProfileAndModelsAsync(
            forceModelRefresh: true,
            setProfileNewThread: false,
            appendWarnings: true).ConfigureAwait(false);
    }

    private async Task ReconnectServiceSessionAsync() {
        StopAutoReconnectLoop();
        await DisposeClientAsync().ConfigureAwait(false);
        await ConnectAsync(fromUserAction: false).ConfigureAwait(false);
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
}
