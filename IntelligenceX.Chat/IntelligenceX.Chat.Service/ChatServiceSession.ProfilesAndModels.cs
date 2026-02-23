using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Profiles;
using IntelligenceX.Copilot;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.CompatibleHttp;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string DefaultRuntimeModel = "gpt-5.3-codex";
    private sealed record SetProfileResult(bool ReconnectClient, bool ModelChanged);
    private sealed record ModelListCacheEntry(string Key, DateTime ExpiresAtUtc, ModelListResult Result);

    private static async ValueTask DisposeClientAsync(IntelligenceXClient? client) {
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
        var defaultModel = (_options.Model ?? string.Empty).Trim();
        if (defaultModel.Length == 0) {
            defaultModel = DefaultRuntimeModel;
        }

        var opts = new IntelligenceXClientOptions {
            TransportKind = _options.OpenAITransport,
            DefaultModel = defaultModel
        };

        if (opts.TransportKind == OpenAITransportKind.Native && !string.IsNullOrWhiteSpace(_instructions)) {
            // Native transport can take the session instructions as a startup hint.
            opts.NativeOptions.Instructions = _instructions!;
        }
        if (opts.TransportKind == OpenAITransportKind.Native) {
            var accountId = (_options.OpenAIAccountId ?? string.Empty).Trim();
            opts.NativeOptions.AuthAccountId = accountId.Length == 0 ? null : accountId;
        }

        if (opts.TransportKind == OpenAITransportKind.CompatibleHttp) {
            opts.CompatibleHttpOptions.BaseUrl = _options.OpenAIBaseUrl;
            opts.CompatibleHttpOptions.AuthMode = _options.OpenAIAuthMode;
            opts.CompatibleHttpOptions.ApiKey = _options.OpenAIApiKey;
            opts.CompatibleHttpOptions.BasicUsername = _options.OpenAIBasicUsername;
            opts.CompatibleHttpOptions.BasicPassword = _options.OpenAIBasicPassword;
            opts.CompatibleHttpOptions.Streaming = _options.OpenAIStreaming;
            opts.CompatibleHttpOptions.AllowInsecureHttp = _options.OpenAIAllowInsecureHttp;
            opts.CompatibleHttpOptions.AllowInsecureHttpNonLoopback = _options.OpenAIAllowInsecureHttpNonLoopback;
        }

        if (opts.TransportKind == OpenAITransportKind.CopilotCli) {
            opts.CopilotOptions.AutoInstallCli = true;
            opts.CopilotOptions.AutoInstallMethod = CopilotCliInstallMethod.Auto;
            var cliPath = Environment.GetEnvironmentVariable("COPILOT_CLI_PATH");
            if (!string.IsNullOrWhiteSpace(cliPath)) {
                opts.CopilotOptions.CliPath = cliPath;
            }
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

    private async Task<SetProfileResult> HandleSetProfileAsync(StreamWriter writer, SetProfileRequest request, CancellationToken cancellationToken) {
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
            AuthMode: _options.OpenAIAuthMode,
            ApiKey: _options.OpenAIApiKey,
            BasicUsername: _options.OpenAIBasicUsername,
            BasicPassword: _options.OpenAIBasicPassword,
            AccountId: _options.OpenAIAccountId,
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
            InvalidateModelListCache();

            var nextClientOptions = BuildClientOptions();
            nextClientOptions.Validate();

            var decision = ResolveRuntimeClientReconfigureDecision(
                previousClientSettings.Transport,
                _options.OpenAITransport,
                previousClientSettings.BaseUrl,
                _options.OpenAIBaseUrl,
                previousClientSettings.AuthMode,
                _options.OpenAIAuthMode,
                previousClientSettings.ApiKey,
                _options.OpenAIApiKey,
                previousClientSettings.BasicUsername,
                _options.OpenAIBasicUsername,
                previousClientSettings.BasicPassword,
                _options.OpenAIBasicPassword,
                previousClientSettings.AccountId,
                _options.OpenAIAccountId,
                previousClientSettings.Streaming,
                _options.OpenAIStreaming,
                previousClientSettings.InsecureHttp,
                _options.OpenAIAllowInsecureHttp,
                previousClientSettings.InsecureHttpNonLoopback,
                _options.OpenAIAllowInsecureHttpNonLoopback,
                previousClientSettings.Model,
                _options.Model);

            await WriteAsync(writer, new AckMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Ok = true,
                Message = $"Active profile set to '{name}'."
            }, cancellationToken).ConfigureAwait(false);

            return new SetProfileResult(ReconnectClient: decision.ReconnectClient, ModelChanged: decision.ModelChanged);
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

    private async Task<SetProfileResult> HandleApplyRuntimeSettingsAsync(StreamWriter writer, ApplyRuntimeSettingsRequest request,
        CancellationToken cancellationToken) {
        if (request is null) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = null,
                Error = "Request payload is required.",
                Code = "invalid_argument"
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

        var previous = _options.ToProfile();
        var previousProfileName = _options.ProfileName;
        var previousClientSettings = (
            Transport: _options.OpenAITransport,
            BaseUrl: _options.OpenAIBaseUrl,
            AuthMode: _options.OpenAIAuthMode,
            ApiKey: _options.OpenAIApiKey,
            BasicUsername: _options.OpenAIBasicUsername,
            BasicPassword: _options.OpenAIBasicPassword,
            AccountId: _options.OpenAIAccountId,
            Streaming: _options.OpenAIStreaming,
            InsecureHttp: _options.OpenAIAllowInsecureHttp,
            InsecureHttpNonLoopback: _options.OpenAIAllowInsecureHttpNonLoopback,
            Model: _options.Model
        );

        try {
            if (!string.IsNullOrWhiteSpace(request.OpenAITransport)) {
                if (!TryParseTransport(request.OpenAITransport, out var parsedTransport)) {
                    await WriteAsync(writer, new ErrorMessage {
                        Kind = ChatServiceMessageKind.Response,
                        RequestId = request.RequestId,
                        Error = "openAITransport must be one of: native, appserver, compatible-http, copilot-cli.",
                        Code = "invalid_argument"
                    }, cancellationToken).ConfigureAwait(false);
                    return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
                }

                _options.OpenAITransport = parsedTransport;
            }

            if (request.OpenAIBaseUrl is not null) {
                var normalizedBaseUrl = request.OpenAIBaseUrl.Trim();
                _options.OpenAIBaseUrl = normalizedBaseUrl.Length == 0 ? null : normalizedBaseUrl;
            }

            if (request.ClearOpenAIApiKey) {
                _options.OpenAIApiKey = null;
            } else if (request.OpenAIApiKey is not null) {
                var normalizedApiKey = request.OpenAIApiKey.Trim();
                _options.OpenAIApiKey = normalizedApiKey.Length == 0 ? null : normalizedApiKey;
            }
            if (request.OpenAIAuthMode is not null) {
                if (!TryParseCompatibleAuthMode(request.OpenAIAuthMode, out var authMode)) {
                    await WriteAsync(writer, new ErrorMessage {
                        Kind = ChatServiceMessageKind.Response,
                        RequestId = request.RequestId,
                        Error = "openAIAuthMode must be one of: bearer, basic, none.",
                        Code = "invalid_argument"
                    }, cancellationToken).ConfigureAwait(false);
                    return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
                }

                _options.OpenAIAuthMode = authMode;
            }
            if (request.ClearOpenAIBasicAuth) {
                _options.OpenAIBasicUsername = null;
                _options.OpenAIBasicPassword = null;
            } else {
                if (request.OpenAIBasicUsername is not null) {
                    var normalizedBasicUser = request.OpenAIBasicUsername.Trim();
                    _options.OpenAIBasicUsername = normalizedBasicUser.Length == 0 ? null : normalizedBasicUser;
                }
                if (request.OpenAIBasicPassword is not null) {
                    var normalizedBasicPassword = request.OpenAIBasicPassword.Trim();
                    _options.OpenAIBasicPassword = normalizedBasicPassword.Length == 0 ? null : normalizedBasicPassword;
                }
            }
            if (request.OpenAIAccountId is not null) {
                var normalizedAccountId = request.OpenAIAccountId.Trim();
                _options.OpenAIAccountId = normalizedAccountId.Length == 0 ? null : normalizedAccountId;
            }

            if (request.OpenAIStreaming.HasValue) {
                _options.OpenAIStreaming = request.OpenAIStreaming.Value;
            }

            if (request.OpenAIAllowInsecureHttp.HasValue) {
                _options.OpenAIAllowInsecureHttp = request.OpenAIAllowInsecureHttp.Value;
            }

            if (request.Model is not null) {
                var normalizedModel = request.Model.Trim();
                if (normalizedModel.Length > 0) {
                    _options.Model = normalizedModel;
                } else {
                    _options.Model = _options.OpenAITransport == OpenAITransportKind.CompatibleHttp
                        ? string.Empty
                        : DefaultRuntimeModel;
                }
            }

            if (request.ReasoningEffort is not null) {
                var normalized = request.ReasoningEffort.Trim();
                if (normalized.Length == 0) {
                    _options.ReasoningEffort = null;
                } else {
                    var parsed = IntelligenceX.OpenAI.Chat.ChatEnumParser.ParseReasoningEffort(normalized);
                    if (!parsed.HasValue) {
                        await WriteAsync(writer, new ErrorMessage {
                            Kind = ChatServiceMessageKind.Response,
                            RequestId = request.RequestId,
                            Error = "reasoningEffort must be one of: minimal, low, medium, high, xhigh.",
                            Code = "invalid_argument"
                        }, cancellationToken).ConfigureAwait(false);
                        return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
                    }

                    _options.ReasoningEffort = parsed.Value;
                }
            }

            if (request.ReasoningSummary is not null) {
                var normalized = request.ReasoningSummary.Trim();
                if (normalized.Length == 0) {
                    _options.ReasoningSummary = null;
                } else {
                    var parsed = IntelligenceX.OpenAI.Chat.ChatEnumParser.ParseReasoningSummary(normalized);
                    if (!parsed.HasValue) {
                        await WriteAsync(writer, new ErrorMessage {
                            Kind = ChatServiceMessageKind.Response,
                            RequestId = request.RequestId,
                            Error = "reasoningSummary must be one of: auto, concise, detailed, off.",
                            Code = "invalid_argument"
                        }, cancellationToken).ConfigureAwait(false);
                        return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
                    }

                    _options.ReasoningSummary = parsed.Value;
                }
            }

            if (request.TextVerbosity is not null) {
                var normalized = request.TextVerbosity.Trim();
                if (normalized.Length == 0) {
                    _options.TextVerbosity = null;
                } else {
                    var parsed = IntelligenceX.OpenAI.Chat.ChatEnumParser.ParseTextVerbosity(normalized);
                    if (!parsed.HasValue) {
                        await WriteAsync(writer, new ErrorMessage {
                            Kind = ChatServiceMessageKind.Response,
                            RequestId = request.RequestId,
                            Error = "textVerbosity must be one of: low, medium, high.",
                            Code = "invalid_argument"
                        }, cancellationToken).ConfigureAwait(false);
                        return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
                    }

                    _options.TextVerbosity = parsed.Value;
                }
            }

            if (request.Temperature.HasValue) {
                var temperature = request.Temperature.Value;
                if (double.IsNaN(temperature) || double.IsInfinity(temperature) || temperature < 0d || temperature > 2d) {
                    await WriteAsync(writer, new ErrorMessage {
                        Kind = ChatServiceMessageKind.Response,
                        RequestId = request.RequestId,
                        Error = "temperature must be between 0 and 2.",
                        Code = "invalid_argument"
                    }, cancellationToken).ConfigureAwait(false);
                    return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
                }

                _options.Temperature = temperature;
            }

            if (request.EnablePowerShellPack.HasValue) {
                _options.EnablePowerShellPack = request.EnablePowerShellPack.Value;
            }
            if (request.EnableTestimoXPack.HasValue) {
                _options.EnableTestimoXPack = request.EnableTestimoXPack.Value;
            }
            if (request.EnableOfficeImoPack.HasValue) {
                _options.EnableOfficeImoPack = request.EnableOfficeImoPack.Value;
            }

            var saveProfileName = (request.ProfileName ?? string.Empty).Trim();
            if (saveProfileName.Length == 0) {
                saveProfileName = (_options.ProfileName ?? string.Empty).Trim();
            }

            if (!_options.NoStateDb && saveProfileName.Length > 0) {
                using var store = new SqliteServiceProfileStore(ResolveStateDbPath());
                await store.UpsertAsync(saveProfileName, _options.ToProfile(), cancellationToken).ConfigureAwait(false);
                _options.ProfileName = saveProfileName;
            }

            _instructions = LoadInstructions(_options);
            RebuildToolingFromOptions();
            InvalidateModelListCache();

            var nextClientOptions = BuildClientOptions();
            nextClientOptions.Validate();

            var decision = ResolveRuntimeClientReconfigureDecision(
                previousClientSettings.Transport,
                _options.OpenAITransport,
                previousClientSettings.BaseUrl,
                _options.OpenAIBaseUrl,
                previousClientSettings.AuthMode,
                _options.OpenAIAuthMode,
                previousClientSettings.ApiKey,
                _options.OpenAIApiKey,
                previousClientSettings.BasicUsername,
                _options.OpenAIBasicUsername,
                previousClientSettings.BasicPassword,
                _options.OpenAIBasicPassword,
                previousClientSettings.AccountId,
                _options.OpenAIAccountId,
                previousClientSettings.Streaming,
                _options.OpenAIStreaming,
                previousClientSettings.InsecureHttp,
                _options.OpenAIAllowInsecureHttp,
                previousClientSettings.InsecureHttpNonLoopback,
                _options.OpenAIAllowInsecureHttpNonLoopback,
                previousClientSettings.Model,
                _options.Model);

            await WriteAsync(writer, new AckMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Ok = true,
                Message = decision.ReconnectClient
                    ? "Runtime settings applied. Provider client will reconnect."
                    : "Runtime settings applied."
            }, cancellationToken).ConfigureAwait(false);

            return new SetProfileResult(ReconnectClient: decision.ReconnectClient, ModelChanged: decision.ModelChanged);
        } catch (Exception ex) {
            try {
                _options.ApplyProfile(previous);
                _options.ProfileName = previousProfileName;
                _instructions = LoadInstructions(_options);
                RebuildToolingFromOptions();
            } catch {
                // Ignore rollback failures.
            }

            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"Failed to apply runtime settings: {ex.Message}",
                Code = "runtime_apply_failed"
            }, cancellationToken).ConfigureAwait(false);

            return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
        }
    }

    private void RebuildToolingFromOptions() {
        RebuildToolingCore(clearRoutingCaches: true);
    }

    [MemberNotNull(
        nameof(_registry),
        nameof(_packs),
        nameof(_packAvailability),
        nameof(_startupWarnings),
        nameof(_pluginSearchPaths),
        nameof(_runtimePolicyDiagnostics))]
    private void RebuildToolingCore(bool clearRoutingCaches) {
        var startupWarnings = new List<string>();
        var runtimePolicyContext = ToolRuntimePolicyBootstrap.CreateContext(
            BuildRuntimePolicyOptions(_options),
            warning => RecordBootstrapWarning(startupWarnings, warning));
        var bootstrapOptions = ToolPackBootstrap.CreateRuntimeBootstrapOptions(
            _options,
            runtimePolicyContext,
            warning => RecordBootstrapWarning(startupWarnings, warning));
        var bootstrapResult = ToolPackBootstrap.CreateDefaultReadOnlyPacksWithAvailability(bootstrapOptions);
        var pluginSearchPaths = NormalizeDistinctStrings(ToolPackBootstrap.GetPluginSearchPaths(bootstrapOptions), maxItems: 32);
        var warnings = NormalizeDistinctStrings(startupWarnings, maxItems: 64);

        var registry = new ToolRegistry();
        _toolPackIdsByToolName.Clear();
        ToolPackBootstrap.RegisterAll(registry, bootstrapResult.Packs, _toolPackIdsByToolName);
        _runtimePolicyDiagnostics = ToolRuntimePolicyBootstrap.ApplyToRegistry(registry, runtimePolicyContext);

        _packs = bootstrapResult.Packs;
        _packAvailability = bootstrapResult.PackAvailability.ToArray();
        _pluginSearchPaths = pluginSearchPaths;
        _startupWarnings = warnings;
        _registry = registry;

        UpdatePackMetadataIndexes(ToolPackBootstrap.GetDescriptors(_packs));
        RebuildPackCapabilityFallbackContracts(registry.GetDefinitions());

        if (clearRoutingCaches) {
            ClearToolRoutingCaches();
        }
    }

    private void ClearToolRoutingCaches() {
        // Tool sets may have changed; clear caches so routing doesn't assume removed tools.
        lock (_toolRoutingStatsLock) {
            _toolRoutingStats.Clear();
        }
        lock (_toolRoutingContextLock) {
            _lastWeightedToolNamesByThreadId.Clear();
            _lastWeightedToolSubsetSeenUtcTicks.Clear();
            _plannerThreadIdByActiveThreadId.Clear();
            _plannerThreadSeenUtcTicksByActiveThreadId.Clear();
        }
        _lastUserIntentByThreadId.Clear();
        _lastUserIntentSeenUtcTicks.Clear();
        _pendingActionsByThreadId.Clear();
        _pendingActionsSeenUtcTicks.Clear();
        _pendingActionsCallToActionTokensByThreadId.Clear();
        _structuredNextActionByThreadId.Clear();
    }

    internal static (bool ReconnectClient, bool ModelChanged) ResolveRuntimeClientReconfigureDecision(
        OpenAITransportKind previousTransport,
        OpenAITransportKind currentTransport,
        string? previousBaseUrl,
        string? currentBaseUrl,
        OpenAICompatibleHttpAuthMode previousAuthMode,
        OpenAICompatibleHttpAuthMode currentAuthMode,
        string? previousApiKey,
        string? currentApiKey,
        string? previousBasicUsername,
        string? currentBasicUsername,
        string? previousBasicPassword,
        string? currentBasicPassword,
        string? previousAccountId,
        string? currentAccountId,
        bool previousStreaming,
        bool currentStreaming,
        bool previousInsecureHttp,
        bool currentInsecureHttp,
        bool previousInsecureHttpNonLoopback,
        bool currentInsecureHttpNonLoopback,
        string? previousModel,
        string? currentModel) {
        var previousBaseUrlNormalized = NormalizeRuntimeBaseUrlForComparison(previousBaseUrl);
        var currentBaseUrlNormalized = NormalizeRuntimeBaseUrlForComparison(currentBaseUrl);
        var reconnect = previousTransport != currentTransport
                        || !string.Equals(previousBaseUrlNormalized, currentBaseUrlNormalized, StringComparison.Ordinal)
                        || previousAuthMode != currentAuthMode
                        || !string.Equals(previousApiKey, currentApiKey, StringComparison.Ordinal)
                        || !string.Equals(previousBasicUsername, currentBasicUsername, StringComparison.Ordinal)
                        || !string.Equals(previousBasicPassword, currentBasicPassword, StringComparison.Ordinal)
                        || !string.Equals(previousAccountId, currentAccountId, StringComparison.Ordinal)
                        || previousStreaming != currentStreaming
                        || previousInsecureHttp != currentInsecureHttp
                        || previousInsecureHttpNonLoopback != currentInsecureHttpNonLoopback;

        var modelChanged = !string.Equals(
            NormalizeRuntimeModelForComparison(previousModel),
            NormalizeRuntimeModelForComparison(currentModel),
            StringComparison.Ordinal);
        return (reconnect, modelChanged);
    }

    internal static string? NormalizeRuntimeBaseUrlForComparison(string? baseUrl) {
        var trimmed = (baseUrl ?? string.Empty).Trim();
        if (trimmed.Length == 0) {
            return null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri is null) {
            return trimmed;
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.IdnHost.ToLowerInvariant();
        var port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port;
        var path = (uri.AbsolutePath ?? string.Empty).TrimEnd('/');
        if (path.Length == 0) {
            path = string.Empty;
        }

        var query = uri.Query ?? string.Empty;
        return scheme + "://" + host + port + path + query;
    }

    private static string? NormalizeRuntimeModelForComparison(string? model) {
        var normalized = (model ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}
