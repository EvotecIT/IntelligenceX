using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Profiles;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private sealed record SetProfileResult(bool ReconnectClient, bool ModelChanged);
    private sealed record ModelListCacheEntry(string Key, DateTime ExpiresAtUtc, ModelListResult Result);

    private static async ValueTask DisposeClientAsync(IntelligenceXClient client) {
        if (client is null) {
            return;
        }
        try {
            await client.DisposeAsync().ConfigureAwait(false);
        } catch {
            try {
                client.Dispose();
            } catch {
                // Ignore.
            }
        }
    }

    private IntelligenceXClientOptions BuildClientOptions() {
        var opts = new IntelligenceXClientOptions {
            TransportKind = _options.OpenAITransport,
            DefaultModel = _options.Model
        };

        if (opts.TransportKind == OpenAITransportKind.Native && !string.IsNullOrWhiteSpace(_instructions)) {
            // Native transport can take the session instructions as a startup hint.
            opts.NativeOptions.Instructions = _instructions!;
        }

        if (opts.TransportKind == OpenAITransportKind.CompatibleHttp) {
            opts.CompatibleHttpOptions.BaseUrl = _options.OpenAIBaseUrl;
            opts.CompatibleHttpOptions.ApiKey = _options.OpenAIApiKey;
            opts.CompatibleHttpOptions.Streaming = _options.OpenAIStreaming;
            opts.CompatibleHttpOptions.AllowInsecureHttp = _options.OpenAIAllowInsecureHttp;
            opts.CompatibleHttpOptions.AllowInsecureHttpNonLoopback = _options.OpenAIAllowInsecureHttpNonLoopback;
        }

        return opts;
    }

    private async Task<IntelligenceXClient> ConnectClientAsync(CancellationToken cancellationToken) {
        var opts = BuildClientOptions();
        // Validate upfront so we can return clear errors for invalid profile settings.
        opts.Validate();
        return await IntelligenceXClient.ConnectAsync(opts, cancellationToken).ConfigureAwait(false);
    }

    private string ResolveStateDbPath() {
        return string.IsNullOrWhiteSpace(_options.StateDbPath)
            ? ServiceOptions.GetDefaultStateDbPath()
            : _options.StateDbPath!;
    }

    private bool HasActiveLoginOrChat(out string? errorCode, out string? error) {
        errorCode = null;
        error = null;

        lock (_loginLock) {
            if (_login is not null && !_login.IsCompleted) {
                errorCode = "login_in_progress";
                error = $"A login flow is already in progress (loginId={_login.LoginId}).";
                return true;
            }
        }

        lock (_chatRunLock) {
            if (_activeChat is not null && !_activeChat.IsCompleted) {
                errorCode = "chat_in_progress";
                error = $"A chat request is already running (requestId={_activeChat.ChatRequestId}).";
                return true;
            }
        }

        return false;
    }

    private async Task HandleListProfilesAsync(StreamWriter writer, ListProfilesRequest request, CancellationToken cancellationToken) {
        if (_options.NoStateDb) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "State DB is disabled; profiles are unavailable.",
                Code = "state_db_disabled"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        try {
            using var store = new SqliteServiceProfileStore(ResolveStateDbPath());
            var names = await store.ListNamesAsync(cancellationToken).ConfigureAwait(false);
            await WriteAsync(writer, new ProfileListMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Profiles = names.Count == 0 ? Array.Empty<string>() : names.ToArray(),
                ActiveProfile = string.IsNullOrWhiteSpace(_options.ProfileName) ? null : _options.ProfileName
            }, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"Failed to list profiles: {ex.Message}",
                Code = "profiles_failed"
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<SetProfileResult> HandleSetProfileAsync(IntelligenceXClient client, StreamWriter writer, SetProfileRequest request,
        CancellationToken cancellationToken) {
        var name = (request.ProfileName ?? string.Empty).Trim();
        if (name.Length == 0) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "profileName is required.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
        }

        if (_options.NoStateDb) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "State DB is disabled; profiles are unavailable.",
                Code = "state_db_disabled"
            }, cancellationToken).ConfigureAwait(false);
            return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
        }

        if (HasActiveLoginOrChat(out var busyCode, out var busyError)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = busyError ?? "Session is busy.",
                Code = busyCode ?? "busy"
            }, cancellationToken).ConfigureAwait(false);
            return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
        }

        ServiceProfile? profile;
        try {
            using var store = new SqliteServiceProfileStore(ResolveStateDbPath());
            profile = await store.GetAsync(name, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"Failed to load profile '{name}': {ex.Message}",
                Code = "profile_failed"
            }, cancellationToken).ConfigureAwait(false);
            return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
        }

        if (profile is null) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"Profile not found: {name}",
                Code = "profile_not_found"
            }, cancellationToken).ConfigureAwait(false);
            return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
        }

        var previous = _options.ToProfile();
        var previousProfileName = _options.ProfileName;
        var previousClientSettings = (
            Transport: _options.OpenAITransport,
            BaseUrl: _options.OpenAIBaseUrl,
            ApiKey: _options.OpenAIApiKey,
            Streaming: _options.OpenAIStreaming,
            InsecureHttp: _options.OpenAIAllowInsecureHttp,
            InsecureHttpNonLoopback: _options.OpenAIAllowInsecureHttpNonLoopback,
            Model: _options.Model
        );

        try {
            _options.ApplyProfile(profile);
            _options.ProfileName = name;
            _instructions = LoadInstructions(_options);
            RebuildToolingFromOptions();
            lock (_modelListCacheLock) {
                _modelListCache = null;
            }

            var nextClientOptions = BuildClientOptions();
            nextClientOptions.Validate();

            var reconnect = previousClientSettings.Transport != _options.OpenAITransport
                            || !string.Equals(previousClientSettings.BaseUrl, _options.OpenAIBaseUrl, StringComparison.Ordinal)
                            || !string.Equals(previousClientSettings.ApiKey, _options.OpenAIApiKey, StringComparison.Ordinal)
                            || previousClientSettings.Streaming != _options.OpenAIStreaming
                            || previousClientSettings.InsecureHttp != _options.OpenAIAllowInsecureHttp
                            || previousClientSettings.InsecureHttpNonLoopback != _options.OpenAIAllowInsecureHttpNonLoopback;

            var modelChanged = !string.Equals(previousClientSettings.Model, _options.Model, StringComparison.Ordinal);

            await WriteAsync(writer, new AckMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Ok = true,
                Message = $"Active profile set to '{name}'."
            }, cancellationToken).ConfigureAwait(false);

            return new SetProfileResult(ReconnectClient: reconnect, ModelChanged: modelChanged);
        } catch (Exception ex) {
            // Restore previous effective options (best-effort).
            try {
                _options.ApplyProfile(previous);
                _options.ProfileName = previousProfileName;
                _instructions = LoadInstructions(_options);
            } catch {
                // Ignore.
            }

            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"Failed to apply profile '{name}': {ex.Message}",
                Code = "profile_apply_failed"
            }, cancellationToken).ConfigureAwait(false);

            return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
        }
    }

    private async Task HandleListModelsAsync(IntelligenceXClient client, StreamWriter writer, ListModelsRequest request, CancellationToken cancellationToken) {
        var profileName = (_options.ProfileName ?? string.Empty).Trim();
        var canUseStateDb = !_options.NoStateDb && !string.IsNullOrWhiteSpace(profileName);
        var favorites = Array.Empty<string>();
        var recents = Array.Empty<string>();
        if (canUseStateDb) {
            try {
                using var prefs = new SqliteModelPreferencesStore(ResolveStateDbPath());
                var favs = await prefs.ListFavoritesAsync(profileName, cancellationToken).ConfigureAwait(false);
                favorites = favs.Count == 0 ? Array.Empty<string>() : favs.ToArray();
                var recentList = await prefs.ListRecentsAsync(profileName, max: 10, cancellationToken).ConfigureAwait(false);
                recents = recentList.Count == 0 ? Array.Empty<string>() : recentList.ToArray();
            } catch {
                // Best-effort; listing models should still work if preferences fail.
            }
        }

        var cacheKey = $"{profileName}|{_options.OpenAITransport}|{_options.OpenAIBaseUrl ?? string.Empty}";
        ModelListResult? cached = null;
        var cachedIsFresh = false;
        lock (_modelListCacheLock) {
            if (_modelListCache is not null && string.Equals(_modelListCache.Key, cacheKey, StringComparison.Ordinal)) {
                cached = _modelListCache.Result;
                cachedIsFresh = DateTime.UtcNow <= _modelListCache.ExpiresAtUtc;
            }
        }

        // ForceRefresh bypasses cache (best-effort).
        if (!request.ForceRefresh && cachedIsFresh && cached is not null) {
            await WriteModelListAsync(writer, request.RequestId, cached, favorites, recents, isStale: false, warning: null, cancellationToken).ConfigureAwait(false);
            return;
        }

        try {
            var result = await client.ListModelsAsync(cancellationToken).ConfigureAwait(false);

            lock (_modelListCacheLock) {
                _modelListCache = new ModelListCacheEntry(
                    Key: cacheKey,
                    ExpiresAtUtc: DateTime.UtcNow.AddMinutes(5),
                    Result: result);
            }

            await WriteModelListAsync(writer, request.RequestId, result, favorites, recents, isStale: false, warning: null, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            // If discovery fails, fall back to cached models (even if stale) to keep the UI usable.
            if (cached is not null) {
                await WriteModelListAsync(writer, request.RequestId, cached, favorites, recents, isStale: true,
                    warning: $"Model discovery failed; returning cached results. Error: {ex.Message}", cancellationToken).ConfigureAwait(false);
                return;
            }

            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"Failed to list models: {ex.Message}",
                Code = "models_failed"
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteModelListAsync(StreamWriter writer, string requestId, ModelListResult result, string[] favorites, string[] recents,
        bool isStale, string? warning, CancellationToken cancellationToken) {
        var models = result.Models.Select(m => new ModelInfoDto {
            Id = m.Id,
            Model = m.Model,
            DisplayName = string.IsNullOrWhiteSpace(m.DisplayName) ? null : m.DisplayName,
            Description = string.IsNullOrWhiteSpace(m.Description) ? null : m.Description,
            IsDefault = m.IsDefault,
            DefaultReasoningEffort = string.IsNullOrWhiteSpace(m.DefaultReasoningEffort) ? null : m.DefaultReasoningEffort,
            SupportedReasoningEfforts = m.SupportedReasoningEfforts.Count == 0
                ? Array.Empty<ReasoningEffortOptionDto>()
                : m.SupportedReasoningEfforts
                    .Select(e => new ReasoningEffortOptionDto {
                        ReasoningEffort = e.ReasoningEffort,
                        Description = string.IsNullOrWhiteSpace(e.Description) ? null : e.Description
                    })
                    .ToArray()
        }).ToArray();

        await WriteAsync(writer, new ModelListMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = requestId,
            Models = models,
            FavoriteModels = favorites ?? Array.Empty<string>(),
            RecentModels = recents ?? Array.Empty<string>(),
            IsStale = isStale,
            Warning = warning,
            NextCursor = result.NextCursor
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleListModelFavoritesAsync(StreamWriter writer, ListModelFavoritesRequest request, CancellationToken cancellationToken) {
        var profileName = (_options.ProfileName ?? string.Empty).Trim();
        if (_options.NoStateDb) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "State DB is disabled; favorites are unavailable.",
                Code = "state_db_disabled"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (string.IsNullOrWhiteSpace(profileName)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "No active profile. Use set_profile first.",
                Code = "no_active_profile"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        try {
            using var prefs = new SqliteModelPreferencesStore(ResolveStateDbPath());
            var favs = await prefs.ListFavoritesAsync(profileName, cancellationToken).ConfigureAwait(false);
            await WriteAsync(writer, new ModelFavoritesMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Models = favs.Count == 0 ? Array.Empty<string>() : favs.ToArray()
            }, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"Failed to list model favorites: {ex.Message}",
                Code = "favorites_failed"
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleSetModelFavoriteAsync(StreamWriter writer, SetModelFavoriteRequest request, CancellationToken cancellationToken) {
        var profileName = (_options.ProfileName ?? string.Empty).Trim();
        var model = (request.Model ?? string.Empty).Trim();
        if (_options.NoStateDb) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "State DB is disabled; favorites are unavailable.",
                Code = "state_db_disabled"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (string.IsNullOrWhiteSpace(profileName)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "No active profile. Use set_profile first.",
                Code = "no_active_profile"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (string.IsNullOrWhiteSpace(model)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = "model is required.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        try {
            using var prefs = new SqliteModelPreferencesStore(ResolveStateDbPath());
            await prefs.SetFavoriteAsync(profileName, model, request.IsFavorite, cancellationToken).ConfigureAwait(false);
            await WriteAsync(writer, new AckMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Ok = true,
                Message = request.IsFavorite ? $"Favorited model '{model}'." : $"Unfavorited model '{model}'."
            }, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"Failed to update model favorite: {ex.Message}",
                Code = "favorites_failed"
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task TryRecordRecentModelAsync(string model, CancellationToken cancellationToken) {
        var profileName = (_options.ProfileName ?? string.Empty).Trim();
        var normalizedModel = (model ?? string.Empty).Trim();
        if (_options.NoStateDb || string.IsNullOrWhiteSpace(profileName) || string.IsNullOrWhiteSpace(normalizedModel)) {
            return;
        }

        try {
            using var prefs = new SqliteModelPreferencesStore(ResolveStateDbPath());
            await prefs.RecordRecentAsync(profileName, normalizedModel, maxRecentsPerProfile: 50, cancellationToken).ConfigureAwait(false);
        } catch {
            // Best-effort.
        }
    }

    private void RebuildToolingFromOptions() {
        var startupWarnings = new List<string>();
        var bootstrapOptions = new ToolPackBootstrapOptions {
            AllowedRoots = _options.AllowedRoots.ToArray(),
            AdDomainController = _options.AdDomainController,
            AdDefaultSearchBaseDn = _options.AdDefaultSearchBaseDn,
            AdMaxResults = _options.AdMaxResults,
            EnablePowerShellPack = _options.EnablePowerShellPack,
            PowerShellAllowWrite = _options.PowerShellAllowWrite,
            EnableTestimoXPack = _options.EnableTestimoXPack,
            EnableDefaultPluginPaths = _options.EnableDefaultPluginPaths,
            PluginPaths = _options.PluginPaths.ToArray(),
            OnBootstrapWarning = warning => RecordBootstrapWarning(startupWarnings, warning)
        };

        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(bootstrapOptions);
        var pluginSearchPaths = NormalizeDistinctStrings(ToolPackBootstrap.GetPluginSearchPaths(bootstrapOptions), maxItems: 32);
        var warnings = NormalizeDistinctStrings(startupWarnings, maxItems: 64);

        var registry = new ToolRegistry();
        ToolPackBootstrap.RegisterAll(registry, packs);

        _packs = packs;
        _pluginSearchPaths = pluginSearchPaths;
        _startupWarnings = warnings;
        _registry = registry;

        UpdatePackMetadataIndexes(ToolPackBootstrap.GetDescriptors(_packs));

        // Tool sets may have changed; clear caches so routing doesn't assume removed tools.
        lock (_toolRoutingStatsLock) {
            _toolRoutingStats.Clear();
        }
        lock (_toolRoutingContextLock) {
            _lastWeightedToolNamesByThreadId.Clear();
            _lastWeightedToolSubsetSeenUtcTicks.Clear();
        }
        _lastUserIntentByThreadId.Clear();
        _lastUserIntentSeenUtcTicks.Clear();
        _pendingActionsByThreadId.Clear();
        _pendingActionsSeenUtcTicks.Clear();
    }
}
