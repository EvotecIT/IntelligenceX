using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Profiles;
using IntelligenceX.OpenAI;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private sealed record SetProfileResult(bool ReconnectClient, bool ModelChanged);

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
        // ForceRefresh is reserved for future server-side caching.
        try {
            var result = await client.ListModelsAsync(cancellationToken).ConfigureAwait(false);
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
                RequestId = request.RequestId,
                Models = models,
                NextCursor = result.NextCursor
            }, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"Failed to list models: {ex.Message}",
                Code = "models_failed"
            }, cancellationToken).ConfigureAwait(false);
        }
    }
}
