using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Profiles;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.CompatibleHttp;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
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

        var cacheKey = BuildModelListCacheKey(profileName);
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
            OwnedBy = string.IsNullOrWhiteSpace(m.OwnedBy) ? null : m.OwnedBy,
            Publisher = string.IsNullOrWhiteSpace(m.Publisher) ? null : m.Publisher,
            Architecture = string.IsNullOrWhiteSpace(m.Architecture) ? null : m.Architecture,
            Quantization = string.IsNullOrWhiteSpace(m.Quantization) ? null : m.Quantization,
            CompatibilityType = string.IsNullOrWhiteSpace(m.CompatibilityType) ? null : m.CompatibilityType,
            RuntimeState = string.IsNullOrWhiteSpace(m.RuntimeState) ? null : m.RuntimeState,
            ModelType = string.IsNullOrWhiteSpace(m.ModelType) ? null : m.ModelType,
            MaxContextLength = m.MaxContextLength,
            LoadedContextLength = m.LoadedContextLength,
            Capabilities = m.Capabilities is { Count: > 0 }
                ? m.Capabilities
                    .Where(capability => !string.IsNullOrWhiteSpace(capability))
                    .Select(capability => capability.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<string>(),
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

    private async Task TryRecordRecentModelAsync(string? model, CancellationToken cancellationToken) {
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

    private void InvalidateModelListCache() {
        lock (_modelListCacheLock) {
            _modelListCache = null;
        }
    }

    private string BuildModelListCacheKey(string profileName) {
        var normalizedProfileName = (profileName ?? string.Empty).Trim();
        var normalizedTransport = _options.OpenAITransport.ToString();
        var normalizedBaseUrl = (_options.OpenAIBaseUrl ?? string.Empty).Trim();
        var normalizedAuthMode = _options.OpenAIAuthMode.ToString();
        var normalizedAccountId = (_options.OpenAIAccountId ?? string.Empty).Trim();
        var normalizedBasicUser = (_options.OpenAIBasicUsername ?? string.Empty).Trim();

        var apiKeyFingerprint = ComputeSecretFingerprint(_options.OpenAIApiKey);
        var basicPasswordFingerprint = ComputeSecretFingerprint(_options.OpenAIBasicPassword);

        return string.Join("|", new[] {
            normalizedProfileName,
            normalizedTransport,
            normalizedBaseUrl,
            normalizedAuthMode,
            normalizedAccountId,
            normalizedBasicUser,
            apiKeyFingerprint,
            basicPasswordFingerprint
        });
    }

    private static string ComputeSecretFingerprint(string? secret) {
        var normalized = (secret ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return "none";
        }

        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash[..8]);
    }

    private static bool TryParseTransport(string? value, out OpenAITransportKind kind) {
        kind = OpenAITransportKind.Native;
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized) {
            case "native":
                kind = OpenAITransportKind.Native;
                return true;
            case "appserver":
            case "app-server":
            case "codex":
                kind = OpenAITransportKind.AppServer;
                return true;
            case "compatible-http":
            case "compatiblehttp":
            case "http":
            case "local":
            case "ollama":
            case "lmstudio":
            case "lm-studio":
                kind = OpenAITransportKind.CompatibleHttp;
                return true;
            case "copilot":
            case "copilot-cli":
            case "github-copilot":
            case "githubcopilot":
                kind = OpenAITransportKind.CopilotCli;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseCompatibleAuthMode(string? value, out OpenAICompatibleHttpAuthMode mode) {
        mode = OpenAICompatibleHttpAuthMode.Bearer;
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized) {
            case "":
            case "bearer":
            case "api-key":
            case "apikey":
            case "token":
                mode = OpenAICompatibleHttpAuthMode.Bearer;
                return true;
            case "basic":
                mode = OpenAICompatibleHttpAuthMode.Basic;
                return true;
            case "none":
            case "off":
                mode = OpenAICompatibleHttpAuthMode.None;
                return true;
            default:
                return false;
        }
    }
}
