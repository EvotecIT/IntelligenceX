using System;
using System.Collections.Generic;
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

    internal static bool ShouldApplyServiceProfile(
        string[] serviceProfileNames,
        string appProfileName,
        string? activeServiceProfileName,
        bool newThread) {
        if (!ContainsProfileName(serviceProfileNames, appProfileName)) {
            return false;
        }

        if (newThread) {
            return true;
        }

        var normalizedActiveProfile = NormalizeProfileName(activeServiceProfileName);
        return !string.Equals(normalizedActiveProfile, appProfileName, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProfileName(string? value) {
        return (value ?? string.Empty).Trim();
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
        var lmStudioTask = ProbeModelsEndpointWithTimeoutAsync(DefaultLmStudioBaseUrl, TimeSpan.FromSeconds(3));
        var ollamaTask = ProbeModelsEndpointWithTimeoutAsync(DefaultOllamaBaseUrl, TimeSpan.FromSeconds(3));
        await Task.WhenAll(lmStudioTask, ollamaTask).ConfigureAwait(false);
        var lmStudio = lmStudioTask.Result;
        var ollama = ollamaTask.Result;

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
                var configuredAvailability = await ProbeModelsEndpointWithTimeoutAsync(configuredBaseUrl, TimeSpan.FromSeconds(3))
                    .ConfigureAwait(false);
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

    private static async Task<ModelsProbeAvailability> ProbeModelsEndpointWithTimeoutAsync(string baseUrl, TimeSpan timeout) {
        var boundedTimeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : timeout;
        using var cts = new CancellationTokenSource(boundedTimeout);
        return await ProbeModelsEndpointAsync(baseUrl, cts.Token).ConfigureAwait(false);
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
