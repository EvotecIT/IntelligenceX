using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
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
    private const string DefaultRuntimeModel = OpenAIModelCatalog.DefaultModel;
    private const string PluginLoadTimingWarningPrefix = "[plugin] load_timing ";
    private const string PluginLoadProgressWarningPrefix = "[plugin] load_progress ";
    private const string PackLoadProgressWarningPrefix = "[startup] pack_load_progress ";
    private const string PackRegistrationProgressWarningPrefix = "[startup] pack_register_progress ";
    private const int SlowPluginSummaryTopCount = 3;
    private const int SlowPackSummaryTopCount = 3;
    private const int SlowPackRegistrationSummaryTopCount = 3;
    private const long SlowPackSummaryThresholdMs = 500;
    private const long SlowPackRegistrationSummaryThresholdMs = 500;
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
            if (_chatRunsByRequestId.Count > 0) {
                if (_activeChat is not null && !_activeChat.IsCompleted) {
                    errorCode = "chat_in_progress";
                    error = $"A chat request is already running (requestId={_activeChat.ChatRequestId}).";
                    return true;
                }

                var queuedCount = _queuedChats.Count;
                if (queuedCount > 0) {
                    errorCode = "chat_in_progress";
                    error = $"Chat execution queue is not empty ({queuedCount} queued turn(s)).";
                    return true;
                }

                errorCode = "chat_in_progress";
                error = "A chat request is already running.";
                return true;
            }
        }

        return false;
    }

    private async Task HandleListProfilesAsync(StreamWriter writer, ListProfilesRequest request, CancellationToken cancellationToken) {
        if (_options.NoStateDb) {
            await WriteAsync(writer, new ProfileListMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Profiles = ServiceProfilePresets.GetBuiltInPresetNames().ToArray(),
                ActiveProfile = string.IsNullOrWhiteSpace(_options.ProfileName) ? null : _options.ProfileName
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        try {
            using var store = new SqliteServiceProfileStore(ResolveStateDbPath());
            var names = await store.ListNamesAsync(cancellationToken).ConfigureAwait(false);
            var availableNames = ServiceProfilePresets.MergeBuiltInPresetNames(names);
            await WriteAsync(writer, new ProfileListMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Profiles = availableNames,
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

        if (HasActiveLoginOrChat(out var busyCode, out var busyError)) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = busyError ?? "Session is busy.",
                Code = busyCode ?? "busy"
            }, cancellationToken).ConfigureAwait(false);
            return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
        }

        var requestedName = name;
        ServiceProfile? profile;
        try {
            if (_options.NoStateDb) {
                var noStateResolution = await ServiceProfilePresets.TryResolveStoredOrBuiltInProfileAsync(
                    requestedName,
                    allowStoredProfiles: false,
                    static (_, _) => Task.FromResult<ServiceProfile?>(null),
                    cancellationToken).ConfigureAwait(false);
                if (!noStateResolution.Success) {
                    await WriteAsync(writer, new ErrorMessage {
                        Kind = ChatServiceMessageKind.Response,
                        RequestId = request.RequestId,
                        Error = noStateResolution.StoredProfilesUnavailable
                            ? "State DB is disabled; saved profiles are unavailable."
                            : $"Profile not found: {requestedName}",
                        Code = noStateResolution.StoredProfilesUnavailable ? "state_db_disabled" : "profile_not_found"
                    }, cancellationToken).ConfigureAwait(false);
                    return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
                }

                name = noStateResolution.ResolvedName;
                profile = noStateResolution.Profile;
            } else {
                using var store = new SqliteServiceProfileStore(ResolveStateDbPath());
                var resolution = await ServiceProfilePresets.TryResolveStoredOrBuiltInProfileAsync(
                    requestedName,
                    allowStoredProfiles: true,
                    (candidateName, ct) => store.GetAsync(candidateName, ct),
                    cancellationToken).ConfigureAwait(false);
                if (!resolution.Success) {
                    await WriteAsync(writer, new ErrorMessage {
                        Kind = ChatServiceMessageKind.Response,
                        RequestId = request.RequestId,
                        Error = $"Profile not found: {requestedName}",
                        Code = "profile_not_found"
                    }, cancellationToken).ConfigureAwait(false);
                    return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
                }

                name = resolution.ResolvedName;
                profile = resolution.Profile;
            }
        } catch (Exception ex) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"Failed to load profile '{requestedName}': {ex.Message}",
                Code = "profile_failed"
            }, cancellationToken).ConfigureAwait(false);
            return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
        }

        if (profile is null) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"Profile not found: {requestedName}",
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

            MarkStartupToolingBootstrapRecoveredAfterRuntimeMutation();
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

            if (request.EnablePackIds is { Length: > 0 } enablePackIds) {
                for (var i = 0; i < enablePackIds.Length; i++) {
                    if (!ServiceOptions.TryApplyPackEnablement(
                            _options,
                            enablePackIds[i],
                            enabled: true,
                            argumentName: "enablePackIds",
                            out var packToggleError)) {
                        await WriteAsync(writer, new ErrorMessage {
                            Kind = ChatServiceMessageKind.Response,
                            RequestId = request.RequestId,
                            Error = packToggleError ?? "Invalid enablePackIds entry.",
                            Code = "invalid_argument"
                        }, cancellationToken).ConfigureAwait(false);
                        return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
                    }
                }
            }
            if (request.DisablePackIds is { Length: > 0 } disablePackIds) {
                for (var i = 0; i < disablePackIds.Length; i++) {
                    if (!ServiceOptions.TryApplyPackEnablement(
                            _options,
                            disablePackIds[i],
                            enabled: false,
                            argumentName: "disablePackIds",
                            out var packToggleError)) {
                        await WriteAsync(writer, new ErrorMessage {
                            Kind = ChatServiceMessageKind.Response,
                            RequestId = request.RequestId,
                            Error = packToggleError ?? "Invalid disablePackIds entry.",
                            Code = "invalid_argument"
                        }, cancellationToken).ConfigureAwait(false);
                        return new SetProfileResult(ReconnectClient: false, ModelChanged: false);
                    }
                }
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

            MarkStartupToolingBootstrapRecoveredAfterRuntimeMutation();
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

    private void ClearPersistedToolingBootstrapPreviewState() {
        _servingPersistedToolingBootstrapPreview = false;
        _persistedPreviewPackSummaries = Array.Empty<ToolPackInfoDto>();
        _persistedPreviewCapabilitySnapshot = null;
        _deferredDescriptorPreviewToolDefinitions = Array.Empty<ToolDefinitionDto>();
    }

    [MemberNotNull(
        nameof(_registry),
        nameof(_packs),
        nameof(_packAvailability),
        nameof(_pluginAvailability),
        nameof(_pluginCatalog),
        nameof(_startupWarnings),
        nameof(_pluginSearchPaths),
        nameof(_runtimePolicyDiagnostics),
        nameof(_routingCatalogDiagnostics),
        nameof(_toolOrchestrationCatalog))]
    private void ApplyLiveToolingBootstrapState(
        ToolRegistry registry,
        ToolDefinitionDto[] toolDefinitions,
        IToolPack[] packs,
        ToolPackAvailabilityInfo[] packAvailability,
        ToolPluginAvailabilityInfo[] pluginAvailability,
        ToolPluginCatalogInfo[] pluginCatalog,
        string[] pluginSearchPaths,
        string[] startupWarnings,
        SessionStartupBootstrapTelemetryDto startupBootstrap,
        ToolRuntimePolicyDiagnostics runtimePolicyDiagnostics,
        ToolRoutingCatalogDiagnostics routingCatalogDiagnostics,
        ToolOrchestrationCatalog toolOrchestrationCatalog) {
        ClearPersistedToolingBootstrapPreviewState();
        _registry = registry;
        _packs = packs;
        _packAvailability = packAvailability;
        _pluginAvailability = pluginAvailability;
        _pluginCatalog = pluginCatalog;
        _pluginSearchPaths = pluginSearchPaths;
        _startupWarnings = startupWarnings;
        _startupBootstrap = startupBootstrap;
        _runtimePolicyDiagnostics = runtimePolicyDiagnostics;
        _routingCatalogDiagnostics = routingCatalogDiagnostics;
        _toolOrchestrationCatalog = toolOrchestrationCatalog;
        UpdatePackMetadataIndexes(ToolPackBootstrap.GetDescriptors(_packs));

        // Publish the cached tool DTOs last so list_tools cannot observe live tool
        // definitions while the session still reports preview-only pack/capability state.
        Volatile.Write(ref _cachedToolDefinitions, toolDefinitions);
    }

    [MemberNotNull(
        nameof(_registry),
        nameof(_packs),
        nameof(_packAvailability),
        nameof(_startupWarnings),
        nameof(_pluginSearchPaths),
        nameof(_runtimePolicyDiagnostics),
        nameof(_routingCatalogDiagnostics),
        nameof(_toolOrchestrationCatalog))]
    private void RebuildToolingCore(bool clearRoutingCaches) {
        var runtimePolicyOptions = BuildRuntimePolicyOptions(_options);
        var resolvedRuntimePolicyOptions = ToolRuntimePolicyBootstrap.ResolveOptions(runtimePolicyOptions);
        if (!clearRoutingCaches && Volatile.Read(ref _cachedToolDefinitions).Length == 0) {
            // Restore lightweight preview state before paying the strict discovery-fingerprint cost.
            TryApplyPersistedToolingBootstrapPreview();
        }

        var bootstrapCacheKey = BuildToolingBootstrapCacheKey(_options, runtimePolicyOptions, resolvedRuntimePolicyOptions);
        if (!clearRoutingCaches
            && _toolingBootstrapCache is not null
            && _toolingBootstrapCache.TryGetSnapshot(bootstrapCacheKey, out var cachedSnapshot)) {
            var cacheHitStopwatch = Stopwatch.StartNew();
            ApplyToolingBootstrapCacheSnapshot(cachedSnapshot, clearRoutingCaches, cacheHitStopwatch.Elapsed);
            return;
        }

        var startupWarnings = new List<string>();
        var totalStopwatch = Stopwatch.StartNew();
        static string FormatElapsed(TimeSpan elapsed) {
            return elapsed.TotalSeconds >= 1
                ? $"{elapsed.TotalSeconds:0.0}s"
                : $"{Math.Max(1, elapsed.TotalMilliseconds):0}ms";
        }

        var runtimePolicyStopwatch = Stopwatch.StartNew();
        var runtimePolicyContext = ToolRuntimePolicyBootstrap.CreateContext(
            runtimePolicyOptions,
            warning => RecordBootstrapWarning(startupWarnings, warning));
        runtimePolicyStopwatch.Stop();

        var bootstrapOptionsStopwatch = Stopwatch.StartNew();
        var bootstrapOptions = ToolPackBootstrap.CreateRuntimeBootstrapOptions(
            _options,
            runtimePolicyContext,
            warning => RecordBootstrapWarning(startupWarnings, warning));
        bootstrapOptionsStopwatch.Stop();

        var packBootstrapStopwatch = Stopwatch.StartNew();
        var bootstrapResult = ToolPackBootstrap.CreateDefaultReadOnlyPacksWithAvailability(bootstrapOptions);
        packBootstrapStopwatch.Stop();
        var pluginSearchPaths = NormalizeDistinctStrings(ToolPackBootstrap.GetPluginSearchPaths(bootstrapOptions), maxItems: 32);
        if (ToolPackBootstrap.IsPluginOnlyModeNoPacks(bootstrapOptions, bootstrapResult.Packs.Count)) {
            RecordBootstrapWarning(
                startupWarnings,
                ToolPackBootstrap.BuildPluginOnlyNoPacksWarning(pluginSearchPaths.Length));
        }

        var registryBuildStopwatch = Stopwatch.StartNew();
        var registry = new ToolRegistry {
            RequireExplicitRoutingMetadata = runtimePolicyContext.Options.RequireExplicitRoutingMetadata
        };
        var packRegisterStopwatch = Stopwatch.StartNew();
        ToolPackBootstrap.RegisterAll(
            registry,
            bootstrapResult.Packs,
            toolPackIdsByToolName: null,
            warning => RecordBootstrapWarning(startupWarnings, warning));
        packRegisterStopwatch.Stop();

        var registryFinalizeStopwatch = Stopwatch.StartNew();
        var definitions = registry.GetDefinitions();
        var toolOrchestrationCatalog = ToolOrchestrationCatalog.Build(definitions, bootstrapResult.Packs);
        var toolDefinitions = BuildToolDefinitionDtosFromRegistryDefinitions(definitions);
        var runtimePolicyDiagnostics = ToolRuntimePolicyBootstrap.ApplyToRegistry(registry, runtimePolicyContext);
        var routingCatalogDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(definitions);
        registryFinalizeStopwatch.Stop();
        registryBuildStopwatch.Stop();

        totalStopwatch.Stop();
        var runtimePolicyMs = Math.Max(1, (long)runtimePolicyStopwatch.Elapsed.TotalMilliseconds);
        var bootstrapOptionsMs = Math.Max(1, (long)bootstrapOptionsStopwatch.Elapsed.TotalMilliseconds);
        var packLoadMs = Math.Max(1, (long)packBootstrapStopwatch.Elapsed.TotalMilliseconds);
        var packRegisterMs = Math.Max(1, (long)packRegisterStopwatch.Elapsed.TotalMilliseconds);
        var registryFinalizeMs = Math.Max(1, (long)registryFinalizeStopwatch.Elapsed.TotalMilliseconds);
        var registryMs = Math.Max(1, (long)registryBuildStopwatch.Elapsed.TotalMilliseconds);
        var totalMs = Math.Max(1, (long)totalStopwatch.Elapsed.TotalMilliseconds);

        var startupPhases = new[] {
            StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhaseRuntimePolicyId, runtimePolicyMs, 1),
            StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhaseBootstrapOptionsId, bootstrapOptionsMs, 2),
            StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhaseDescriptorDiscoveryId, packLoadMs, 3),
            StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhasePackActivationId, packRegisterMs, 4),
            StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhaseRegistryActivationFinalizeId, registryFinalizeMs, 5)
        };
        var slowestPhase = startupPhases
            .OrderByDescending(static phase => phase.DurationMs)
            .ThenBy(static phase => phase.Order)
            .FirstOrDefault();

        var availability = bootstrapResult.PackAvailability;
        var disabledPackCount = 0;
        for (var i = 0; i < availability.Count; i++) {
            if (!availability[i].Enabled) {
                disabledPackCount++;
            }
        }

        if (totalStopwatch.Elapsed >= TimeSpan.FromMilliseconds(600)) {
            RecordBootstrapWarning(
                startupWarnings,
                StartupBootstrapWarningBuilder.BuildTimingSummary(
                    FormatElapsed(totalStopwatch.Elapsed),
                    FormatElapsed(runtimePolicyStopwatch.Elapsed),
                    FormatElapsed(bootstrapOptionsStopwatch.Elapsed),
                    FormatElapsed(packBootstrapStopwatch.Elapsed),
                    FormatElapsed(packRegisterStopwatch.Elapsed),
                    FormatElapsed(registryFinalizeStopwatch.Elapsed),
                    FormatElapsed(registryBuildStopwatch.Elapsed),
                    definitions.Count,
                    bootstrapResult.Packs.Count,
                    disabledPackCount,
                    pluginSearchPaths.Length));
        }

        var warningSummary = SummarizeStartupLoadWarnings(startupWarnings);
        var warnings = NormalizeDistinctStrings(startupWarnings, maxItems: 64);

        var packs = bootstrapResult.Packs.ToArray();
        var packAvailability = bootstrapResult.PackAvailability.ToArray();
        var pluginAvailability = bootstrapResult.PluginAvailability.ToArray();
        var pluginCatalog = bootstrapResult.PluginCatalog.ToArray();
        var startupBootstrap = StartupBootstrapContracts.WithCanonicalPhaseDurations(new SessionStartupBootstrapTelemetryDto {
            TotalMs = totalMs,
            RuntimePolicyMs = runtimePolicyMs,
            BootstrapOptionsMs = bootstrapOptionsMs,
            PackLoadMs = packLoadMs,
            PackRegisterMs = packRegisterMs,
            RegistryFinalizeMs = registryFinalizeMs,
            RegistryMs = registryMs,
            Tools = definitions.Count,
            PacksLoaded = bootstrapResult.Packs.Count,
            PacksDisabled = disabledPackCount,
            PluginRoots = pluginSearchPaths.Length,
            SlowPackCount = warningSummary.SlowPackCount,
            SlowPackTopCount = warningSummary.SlowPackTopCount,
            PackProgressProcessed = warningSummary.PackProgressProcessed,
            PackProgressTotal = warningSummary.PackProgressTotal,
            SlowPackRegistrationCount = warningSummary.SlowPackRegistrationCount,
            SlowPackRegistrationTopCount = warningSummary.SlowPackRegistrationTopCount,
            PackRegistrationProgressProcessed = warningSummary.PackRegistrationProgressProcessed,
            PackRegistrationProgressTotal = warningSummary.PackRegistrationProgressTotal,
            SlowPluginCount = warningSummary.SlowPluginCount,
            SlowPluginTopCount = warningSummary.SlowPluginTopCount,
            PluginProgressProcessed = warningSummary.PluginProgressProcessed,
            PluginProgressTotal = warningSummary.PluginProgressTotal,
            Phases = startupPhases,
            SlowestPhaseId = slowestPhase?.Id,
            SlowestPhaseLabel = slowestPhase?.Label,
            SlowestPhaseMs = slowestPhase?.DurationMs ?? 0
        });
        // Persist the exact deferred descriptor-preview fingerprint used by warm-path validation.
        var previewDiscoveryFingerprint = BuildToolingBootstrapPreviewFingerprint(_options, runtimePolicyOptions);
        ApplyLiveToolingBootstrapState(
            registry,
            toolDefinitions,
            packs,
            packAvailability,
            pluginAvailability,
            pluginCatalog,
            pluginSearchPaths,
            warnings,
            startupBootstrap,
            runtimePolicyDiagnostics,
            routingCatalogDiagnostics,
            toolOrchestrationCatalog);

        _toolingBootstrapCache?.StoreSnapshot(
            bootstrapCacheKey,
            new ChatServiceToolingBootstrapSnapshot {
                Registry = registry,
                ToolDefinitions = toolDefinitions,
                PackSummaries = BuildPackPolicyList(packAvailability, toolOrchestrationCatalog),
                Packs = packs,
                PackAvailability = packAvailability,
                PluginAvailability = bootstrapResult.PluginAvailability.ToArray(),
                PluginCatalog = bootstrapResult.PluginCatalog.ToArray(),
                StartupWarnings = warnings,
                StartupBootstrap = startupBootstrap,
                PluginSearchPaths = pluginSearchPaths,
                RuntimePolicyDiagnostics = runtimePolicyDiagnostics,
                RoutingCatalogDiagnostics = routingCatalogDiagnostics,
                CapabilitySnapshot = BuildRuntimeCapabilitySnapshot(),
                ToolOrchestrationCatalog = toolOrchestrationCatalog
            },
            previewDiscoveryFingerprint: previewDiscoveryFingerprint);

        if (clearRoutingCaches) {
            ClearToolRoutingCaches();
        }
    }

    [MemberNotNull(
        nameof(_registry),
        nameof(_packs),
        nameof(_packAvailability),
        nameof(_pluginAvailability),
        nameof(_pluginCatalog),
        nameof(_startupWarnings),
        nameof(_pluginSearchPaths),
        nameof(_runtimePolicyDiagnostics),
        nameof(_routingCatalogDiagnostics),
        nameof(_toolOrchestrationCatalog))]
    private void ApplyToolingBootstrapCacheSnapshot(
        ChatServiceToolingBootstrapSnapshot snapshot,
        bool clearRoutingCaches,
        TimeSpan cacheHitElapsed) {
        var cacheHitMs = Math.Max(1, (long)Math.Round(Math.Max(1, cacheHitElapsed.TotalMilliseconds)));
        var warnings = new List<string>(snapshot.StartupWarnings.Length + 1);
        warnings.AddRange(snapshot.StartupWarnings);
        warnings.Add(StartupBootstrapWarningBuilder.BuildCacheHitSummary(
            cacheHitMs,
            snapshot.StartupBootstrap.Tools,
            snapshot.StartupBootstrap.PacksLoaded));
        var startupWarnings = NormalizeDistinctStrings(warnings, maxItems: 64);
        var startupBootstrap = StartupBootstrapContracts.WithCanonicalPhaseDurations(snapshot.StartupBootstrap with {
            TotalMs = cacheHitMs,
            RuntimePolicyMs = cacheHitMs,
            BootstrapOptionsMs = 0,
            PackLoadMs = 0,
            PackRegisterMs = 0,
            RegistryFinalizeMs = 0,
            RegistryMs = cacheHitMs,
            Phases = new[] {
                StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhaseCacheHitId, cacheHitMs, 1)
            },
            SlowestPhaseId = StartupBootstrapContracts.PhaseCacheHitId,
            SlowestPhaseLabel = StartupBootstrapContracts.PhaseCacheHitLabel,
            SlowestPhaseMs = cacheHitMs
        });
        ApplyLiveToolingBootstrapState(
            snapshot.Registry,
            snapshot.ToolDefinitions,
            snapshot.Packs,
            snapshot.PackAvailability.ToArray(),
            snapshot.PluginAvailability.ToArray(),
            snapshot.PluginCatalog.ToArray(),
            snapshot.PluginSearchPaths.ToArray(),
            startupWarnings,
            startupBootstrap,
            snapshot.RuntimePolicyDiagnostics,
            snapshot.RoutingCatalogDiagnostics,
            snapshot.ToolOrchestrationCatalog);

        if (clearRoutingCaches) {
            ClearToolRoutingCaches();
        }
    }

    private bool TryApplyPersistedToolingBootstrapPreview() {
        if (_toolingBootstrapCache is null) {
            return false;
        }

        var runtimePolicyOptions = BuildRuntimePolicyOptions(_options);
        var previewCacheKey = BuildToolingBootstrapPreviewCacheKey(
            _options,
            runtimePolicyOptions,
            ToolRuntimePolicyBootstrap.ResolveOptions(runtimePolicyOptions));
        var previewDiscoveryFingerprint = BuildToolingBootstrapPreviewFingerprint(_options, runtimePolicyOptions);
        if (!_toolingBootstrapCache.TryGetPersistedPreviewSnapshot(
                previewCacheKey,
                previewDiscoveryFingerprint,
                out var persistedSnapshot)) {
            return false;
        }

        ApplyToolingBootstrapPersistedSnapshot(persistedSnapshot);
        return true;
    }

    private void ApplyToolingBootstrapPersistedSnapshot(ChatServiceToolingBootstrapPersistedSnapshot snapshot) {
        _servingPersistedToolingBootstrapPreview = true;
        _persistedPreviewPackSummaries = snapshot.PackSummaries ?? Array.Empty<ToolPackInfoDto>();
        _persistedPreviewCapabilitySnapshot = snapshot.CapabilitySnapshot;
        Volatile.Write(ref _cachedToolDefinitions, snapshot.ToolDefinitions);
        _packAvailability = snapshot.PackAvailability.ToArray();
        _pluginAvailability = snapshot.PluginAvailability.ToArray();
        _pluginCatalog = snapshot.PluginCatalog.ToArray();
        _pluginSearchPaths = snapshot.PluginSearchPaths.ToArray();
        _runtimePolicyDiagnostics = snapshot.RuntimePolicyDiagnostics;
        _routingCatalogDiagnostics = snapshot.RoutingCatalogDiagnostics;
        _startupBootstrap = BuildPersistedPreviewStartupBootstrap(snapshot);
        UpdatePackMetadataIndexesFromAvailability(_packAvailability);

        var warnings = new List<string>(snapshot.StartupWarnings.Length + 1);
        warnings.AddRange(snapshot.StartupWarnings);
        warnings.Add(StartupBootstrapWarningBuilder.BuildPersistedPreviewRestoredSummary());
        _startupWarnings = NormalizeDistinctStrings(warnings, maxItems: 64);
    }

    private static string BuildToolingBootstrapCacheKey(
        ServiceOptions options,
        ToolRuntimePolicyOptions runtimePolicyOptions,
        ToolRuntimePolicyResolvedOptions resolvedRuntimePolicyOptions) {
        var builder = BuildToolingBootstrapCacheKeyBuilder(options, runtimePolicyOptions, resolvedRuntimePolicyOptions);
        var runtimePolicyContext = ToolRuntimePolicyBootstrap.CreateContext(runtimePolicyOptions);
        var bootstrapOptions = ToolPackBootstrap.CreateRuntimeBootstrapOptions(options, runtimePolicyContext);
        builder.Append("discovery_fingerprint=").Append(ToolPackBootstrap.BuildDiscoveryFingerprint(bootstrapOptions)).Append(';');
        return builder.ToString();
    }

    private static string BuildToolingBootstrapPreviewCacheKey(
        ServiceOptions options,
        ToolRuntimePolicyOptions runtimePolicyOptions,
        ToolRuntimePolicyResolvedOptions resolvedRuntimePolicyOptions) {
        return BuildToolingBootstrapCacheKeyBuilder(options, runtimePolicyOptions, resolvedRuntimePolicyOptions).ToString();
    }

    private static string BuildToolingBootstrapPreviewFingerprint(
        ServiceOptions options,
        ToolRuntimePolicyOptions runtimePolicyOptions) {
        var runtimePolicyContext = ToolRuntimePolicyBootstrap.CreateContext(runtimePolicyOptions);
        var bootstrapOptions = ToolPackBootstrap.CreateRuntimeBootstrapOptions(options, runtimePolicyContext);
        return ToolPackBootstrap.BuildDeferredDescriptorPreviewFingerprint(bootstrapOptions);
    }

    private static SessionStartupBootstrapTelemetryDto BuildPersistedPreviewStartupBootstrap(
        ChatServiceToolingBootstrapPersistedSnapshot snapshot) {
        var toolDefinitions = snapshot.ToolDefinitions ?? Array.Empty<ToolDefinitionDto>();
        var packAvailability = snapshot.PackAvailability ?? Array.Empty<ToolPackAvailabilityInfo>();
        var pluginSearchPaths = snapshot.PluginSearchPaths ?? Array.Empty<string>();
        var enabledPackCount = 0;
        var disabledPackCount = 0;
        for (var i = 0; i < packAvailability.Length; i++) {
            if (packAvailability[i].Enabled) {
                enabledPackCount++;
            } else {
                disabledPackCount++;
            }
        }

        const long previewRestoreMs = 1;
        return StartupBootstrapContracts.WithCanonicalPhaseDurations(new SessionStartupBootstrapTelemetryDto {
            TotalMs = previewRestoreMs,
            RuntimePolicyMs = 0,
            BootstrapOptionsMs = 0,
            PackLoadMs = 0,
            PackRegisterMs = 0,
            RegistryFinalizeMs = 0,
            RegistryMs = 0,
            Tools = toolDefinitions.Length,
            PacksLoaded = enabledPackCount,
            PacksDisabled = disabledPackCount,
            PluginRoots = pluginSearchPaths.Length,
            Phases = new[] {
                StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhaseDescriptorCacheHitId, previewRestoreMs, 1)
            },
            SlowestPhaseId = StartupBootstrapContracts.PhaseDescriptorCacheHitId,
            SlowestPhaseLabel = StartupBootstrapContracts.PhaseDescriptorCacheHitLabel,
            SlowestPhaseMs = previewRestoreMs
        });
    }

    private static StringBuilder BuildToolingBootstrapCacheKeyBuilder(
        ServiceOptions options,
        ToolRuntimePolicyOptions runtimePolicyOptions,
        ToolRuntimePolicyResolvedOptions resolvedRuntimePolicyOptions) {
        static string Normalize(string? value) {
            return (value ?? string.Empty).Trim();
        }

        static void AppendStringList(StringBuilder builder, string key, IEnumerable<string>? values, bool normalizePackId = false) {
            var normalized = values is null
                ? Array.Empty<string>()
                : values
                    .Select(static value => value ?? string.Empty)
                    .Select(value => normalizePackId ? ToolPackBootstrap.NormalizePackId(value) : value.Trim())
                    .Where(static value => value.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            builder.Append(key);
            builder.Append('=');
            builder.Append(string.Join("|", normalized));
            builder.Append(';');
        }

        var builder = new StringBuilder(capacity: 768);
        builder.Append("ad_dc=").Append(Normalize(options.AdDomainController)).Append(';');
        builder.Append("ad_base=").Append(Normalize(options.AdDefaultSearchBaseDn)).Append(';');
        builder.Append("ad_max=").Append(options.AdMaxResults.ToString(CultureInfo.InvariantCulture)).Append(';');
        builder.Append("ps_allow_write=").Append(options.PowerShellAllowWrite ? '1' : '0').Append(';');
        builder.Append("built_in_packs=").Append(options.EnableBuiltInPackLoading ? '1' : '0').Append(';');
        builder.Append("default_built_in_assemblies=").Append(options.UseDefaultBuiltInToolAssemblyNames ? '1' : '0').Append(';');
        builder.Append("workspace_builtin_output_probing=").Append(options.EnableWorkspaceBuiltInToolOutputProbing ? '1' : '0').Append(';');
        builder.Append("default_plugin_paths=").Append(options.EnableDefaultPluginPaths ? '1' : '0').Append(';');
        AppendStringList(builder, "allowed_roots", options.AllowedRoots);
        AppendStringList(builder, "built_in_tool_assemblies", options.BuiltInToolAssemblyNames);
        AppendStringList(builder, "plugin_paths", options.GetEffectivePluginPaths());
        AppendStringList(builder, "enabled_packs", options.EnabledPackIds, normalizePackId: true);
        AppendStringList(builder, "disabled_packs", options.DisabledPackIds, normalizePackId: true);
        builder.Append("write_mode=").Append(runtimePolicyOptions.WriteGovernanceMode.ToString()).Append(';');
        builder.Append("require_write_runtime=").Append(runtimePolicyOptions.RequireWriteGovernanceRuntime ? '1' : '0').Append(';');
        builder.Append("require_write_audit=").Append(runtimePolicyOptions.RequireWriteAuditSinkForWriteOperations ? '1' : '0').Append(';');
        builder.Append("write_audit_mode=").Append(runtimePolicyOptions.WriteAuditSinkMode.ToString()).Append(';');
        builder.Append("write_audit_path=").Append(Normalize(runtimePolicyOptions.WriteAuditSinkPath)).Append(';');
        builder.Append("auth_preset=").Append(runtimePolicyOptions.AuthenticationPreset.ToString()).Append(';');
        builder.Append("require_explicit_routing=").Append(runtimePolicyOptions.RequireExplicitRoutingMetadata ? '1' : '0').Append(';');
        builder.Append("require_auth_runtime=").Append(runtimePolicyOptions.RequireAuthenticationRuntime ? '1' : '0').Append(';');
        builder.Append("require_smtp_probe=").Append(resolvedRuntimePolicyOptions.RequireSuccessfulSmtpProbeForSend ? '1' : '0').Append(';');
        builder.Append("smtp_probe_max_age_seconds=").Append(resolvedRuntimePolicyOptions.SmtpProbeMaxAgeSeconds.ToString(CultureInfo.InvariantCulture)).Append(';');
        builder.Append("runas_profile_path=").Append(Normalize(runtimePolicyOptions.RunAsProfilePath)).Append(';');
        builder.Append("auth_profile_path=").Append(Normalize(runtimePolicyOptions.AuthenticationProfilePath)).Append(';');
        return builder;
    }

    private void ClearToolRoutingCaches(bool preserveConversationState = false) {
        // Tool sets may have changed; clear caches so routing doesn't assume removed tools.
        lock (_toolRoutingStatsLock) {
            _toolRoutingStats.Clear();
        }
        lock (_toolRoutingContextLock) {
            _lastWeightedToolNamesByThreadId.Clear();
            _lastWeightedToolSubsetSeenUtcTicks.Clear();
            _plannerThreadIdByActiveThreadId.Clear();
            _plannerThreadSeenUtcTicksByActiveThreadId.Clear();
            if (!preserveConversationState) {
                _domainIntentFamilyByThreadId.Clear();
                _domainIntentFamilySeenUtcTicks.Clear();
                _pendingDomainIntentClarificationSeenUtcTicks.Clear();
                _packPreflightToolNamesByThreadId.Clear();
                _packPreflightSeenUtcTicks.Clear();
                _structuredNextActionAutoReplayByThreadId.Clear();
            }
        }
        if (!preserveConversationState) {
            _lastUserIntentByThreadId.Clear();
            _lastUserIntentSeenUtcTicks.Clear();
            _pendingActionsByThreadId.Clear();
            _pendingActionsSeenUtcTicks.Clear();
            _pendingActionsCallToActionTokensByThreadId.Clear();
            _structuredNextActionByThreadId.Clear();
            ClearRecoveredThreadAliases();
            ClearThreadToolEvidence();
            ClearThreadBackgroundWorkSnapshots();
            ClearPendingActionsSnapshots();
            ClearUserIntentSnapshots();
            ClearDomainIntentFamilySnapshots();
            ClearPendingDomainIntentClarificationSnapshots();
            ClearPackPreflightSnapshots();
            ClearHostBootstrapFailureSnapshots();
            ClearAlternateEngineHealthSnapshots();
            ClearStructuredNextActionSnapshots();
            ClearWorkingMemoryCheckpoints();
        }
        ClearWeightedToolSubsetSnapshots();
        ClearPlannerThreadContextSnapshots();
        ClearToolRoutingStatsSnapshots();
    }

    private static StartupLoadWarningSummary SummarizeStartupLoadWarnings(List<string> startupWarnings) {
        if (startupWarnings is null || startupWarnings.Count == 0) {
            return default;
        }

        var retainedWarnings = new List<string>(startupWarnings.Count);
        var pluginEntries = new Dictionary<string, SlowPluginLoadEntry>(StringComparer.OrdinalIgnoreCase);
        var packEntries = new Dictionary<string, SlowPackLoadEntry>(StringComparer.OrdinalIgnoreCase);
        var packRegistrationEntries = new Dictionary<string, SlowPackRegistrationEntry>(StringComparer.OrdinalIgnoreCase);

        var pluginProgressBeginCount = 0;
        var pluginProgressEndCount = 0;
        var pluginProgressLatestIndex = 0;
        var pluginProgressLatestTotal = 0;

        var packProgressBeginCount = 0;
        var packProgressEndCount = 0;
        var packProgressLatestIndex = 0;
        var packProgressLatestTotal = 0;

        var packRegistrationBeginCount = 0;
        var packRegistrationEndCount = 0;
        var packRegistrationLatestIndex = 0;
        var packRegistrationLatestTotal = 0;

        for (var i = 0; i < startupWarnings.Count; i++) {
            var warning = startupWarnings[i];
            if (TryParseSlowPluginLoadWarning(warning, out var pluginEntry)) {
                if (!pluginEntries.TryGetValue(pluginEntry.PluginId, out var existingPlugin) || existingPlugin.ElapsedMs < pluginEntry.ElapsedMs) {
                    pluginEntries[pluginEntry.PluginId] = pluginEntry;
                }
                continue;
            }

            if (TryParsePluginLoadProgressWarning(warning, out var pluginProgressEntry)) {
                if (pluginProgressEntry.IsBegin) {
                    pluginProgressBeginCount++;
                } else {
                    pluginProgressEndCount++;
                }

                if (pluginProgressEntry.Index > pluginProgressLatestIndex) {
                    pluginProgressLatestIndex = pluginProgressEntry.Index;
                }
                if (pluginProgressEntry.Total > pluginProgressLatestTotal) {
                    pluginProgressLatestTotal = pluginProgressEntry.Total;
                }
                continue;
            }

            if (TryParsePackLoadProgressWarning(warning, out var packProgressEntry)) {
                if (packProgressEntry.IsBegin) {
                    packProgressBeginCount++;
                } else {
                    packProgressEndCount++;
                    if (packProgressEntry.ElapsedMs >= SlowPackSummaryThresholdMs) {
                        if (!packEntries.TryGetValue(packProgressEntry.PackId, out var existingPack)
                            || existingPack.ElapsedMs < packProgressEntry.ElapsedMs) {
                            packEntries[packProgressEntry.PackId] = new SlowPackLoadEntry(
                                PackId: packProgressEntry.PackId,
                                ElapsedMs: packProgressEntry.ElapsedMs,
                                Failed: packProgressEntry.Failed);
                        }
                    }
                }

                if (packProgressEntry.Index > packProgressLatestIndex) {
                    packProgressLatestIndex = packProgressEntry.Index;
                }
                if (packProgressEntry.Total > packProgressLatestTotal) {
                    packProgressLatestTotal = packProgressEntry.Total;
                }
                continue;
            }

            if (TryParsePackRegistrationProgressWarning(warning, out var packRegistrationEntry)) {
                if (packRegistrationEntry.IsBegin) {
                    packRegistrationBeginCount++;
                } else {
                    packRegistrationEndCount++;
                    if (packRegistrationEntry.ElapsedMs >= SlowPackRegistrationSummaryThresholdMs) {
                        if (!packRegistrationEntries.TryGetValue(packRegistrationEntry.PackId, out var existingRegistration)
                            || existingRegistration.ElapsedMs < packRegistrationEntry.ElapsedMs) {
                            packRegistrationEntries[packRegistrationEntry.PackId] = new SlowPackRegistrationEntry(
                                PackId: packRegistrationEntry.PackId,
                                ElapsedMs: packRegistrationEntry.ElapsedMs,
                                ToolsRegistered: packRegistrationEntry.ToolsRegistered,
                                Failed: packRegistrationEntry.Failed);
                        }
                    }
                }

                if (packRegistrationEntry.Index > packRegistrationLatestIndex) {
                    packRegistrationLatestIndex = packRegistrationEntry.Index;
                }
                if (packRegistrationEntry.Total > packRegistrationLatestTotal) {
                    packRegistrationLatestTotal = packRegistrationEntry.Total;
                }
                continue;
            }

            retainedWarnings.Add(warning);
        }

        if (pluginEntries.Count > 0) {
            var orderedPlugins = pluginEntries.Values
                .OrderByDescending(static e => e.ElapsedMs)
                .ThenBy(static e => e.PluginId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var pluginTopCount = Math.Min(SlowPluginSummaryTopCount, orderedPlugins.Length);

            var pluginSegments = new List<string>(pluginTopCount);
            for (var i = 0; i < pluginTopCount; i++) {
                var entry = orderedPlugins[i];
                pluginSegments.Add($"{entry.PluginId}={entry.ElapsedMs}ms (loaded={entry.Loaded}, disabled={entry.Disabled}, failed={entry.Failed})");
            }

            retainedWarnings.Add(StartupBootstrapWarningBuilder.BuildSlowPluginLoadsTop(pluginTopCount, orderedPlugins.Length, pluginSegments));
            if (orderedPlugins.Length > pluginTopCount) {
                retainedWarnings.Add(StartupBootstrapWarningBuilder.BuildAdditionalSlowPluginsOmitted(orderedPlugins.Length - pluginTopCount));
            }
        }

        if (packEntries.Count > 0) {
            var orderedPacks = packEntries.Values
                .OrderByDescending(static e => e.ElapsedMs)
                .ThenBy(static e => e.PackId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var packTopCount = Math.Min(SlowPackSummaryTopCount, orderedPacks.Length);

            var packSegments = new List<string>(packTopCount);
            for (var i = 0; i < packTopCount; i++) {
                var entry = orderedPacks[i];
                var failedToken = entry.Failed ? ", failed=1" : string.Empty;
                packSegments.Add($"{entry.PackId}={entry.ElapsedMs}ms{failedToken}");
            }

            retainedWarnings.Add(StartupBootstrapWarningBuilder.BuildSlowPackLoadsTop(packTopCount, orderedPacks.Length, packSegments));
            if (orderedPacks.Length > packTopCount) {
                retainedWarnings.Add(StartupBootstrapWarningBuilder.BuildAdditionalSlowPacksOmitted(orderedPacks.Length - packTopCount));
            }
        }

        if (packRegistrationEntries.Count > 0) {
            var orderedRegistrations = packRegistrationEntries.Values
                .OrderByDescending(static e => e.ElapsedMs)
                .ThenBy(static e => e.PackId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var registrationTopCount = Math.Min(SlowPackRegistrationSummaryTopCount, orderedRegistrations.Length);

            var registrationSegments = new List<string>(registrationTopCount);
            for (var i = 0; i < registrationTopCount; i++) {
                var entry = orderedRegistrations[i];
                var failedToken = entry.Failed ? ", failed=1" : string.Empty;
                registrationSegments.Add($"{entry.PackId}={entry.ElapsedMs}ms (tools={entry.ToolsRegistered}{failedToken})");
            }

            retainedWarnings.Add(StartupBootstrapWarningBuilder.BuildSlowPackRegistrationsTop(registrationTopCount, orderedRegistrations.Length, registrationSegments));
            if (orderedRegistrations.Length > registrationTopCount) {
                retainedWarnings.Add(StartupBootstrapWarningBuilder.BuildAdditionalSlowPackRegistrationsOmitted(orderedRegistrations.Length - registrationTopCount));
            }
        }

        if (pluginProgressBeginCount > 0 || pluginProgressEndCount > 0) {
            AppendPluginLoadProgressSummary(
                retainedWarnings,
                pluginProgressBeginCount,
                pluginProgressEndCount,
                pluginProgressLatestIndex,
                pluginProgressLatestTotal);
        }

        if (packProgressBeginCount > 0 || packProgressEndCount > 0) {
            AppendPackLoadProgressSummary(
                retainedWarnings,
                packProgressBeginCount,
                packProgressEndCount,
                packProgressLatestIndex,
                packProgressLatestTotal);
        }

        if (packRegistrationBeginCount > 0 || packRegistrationEndCount > 0) {
            AppendPackRegistrationProgressSummary(
                retainedWarnings,
                packRegistrationBeginCount,
                packRegistrationEndCount,
                packRegistrationLatestIndex,
                packRegistrationLatestTotal);
        }

        startupWarnings.Clear();
        startupWarnings.AddRange(retainedWarnings);
        var pluginProcessed = ResolvePluginProgressProcessed(pluginProgressEndCount, pluginProgressLatestIndex);
        var pluginTotal = ResolvePluginProgressTotal(pluginProgressBeginCount, pluginProgressEndCount, pluginProgressLatestTotal, pluginProcessed);
        var packProcessed = ResolvePackProgressProcessed(packProgressEndCount, packProgressLatestIndex);
        var packTotal = ResolvePackProgressTotal(packProgressBeginCount, packProgressEndCount, packProgressLatestTotal, packProcessed);
        var packRegistrationProcessed = ResolvePackRegistrationProgressProcessed(packRegistrationEndCount, packRegistrationLatestIndex);
        var packRegistrationTotal = ResolvePackRegistrationProgressTotal(
            packRegistrationBeginCount,
            packRegistrationEndCount,
            packRegistrationLatestTotal,
            packRegistrationProcessed);
        var pluginTop = pluginEntries.Count == 0 ? 0 : Math.Min(SlowPluginSummaryTopCount, pluginEntries.Count);
        var packTop = packEntries.Count == 0 ? 0 : Math.Min(SlowPackSummaryTopCount, packEntries.Count);
        var packRegistrationTop = packRegistrationEntries.Count == 0 ? 0 : Math.Min(SlowPackRegistrationSummaryTopCount, packRegistrationEntries.Count);
        return new StartupLoadWarningSummary(
            SlowPluginCount: pluginEntries.Count,
            SlowPluginTopCount: pluginTop,
            PluginProgressProcessed: pluginProcessed,
            PluginProgressTotal: pluginTotal,
            SlowPackCount: packEntries.Count,
            SlowPackTopCount: packTop,
            PackProgressProcessed: packProcessed,
            PackProgressTotal: packTotal,
            SlowPackRegistrationCount: packRegistrationEntries.Count,
            SlowPackRegistrationTopCount: packRegistrationTop,
            PackRegistrationProgressProcessed: packRegistrationProcessed,
            PackRegistrationProgressTotal: packRegistrationTotal);
    }

    private static StartupLoadWarningSummary SummarizeSlowPluginLoadWarnings(List<string> startupWarnings) {
        return SummarizeStartupLoadWarnings(startupWarnings);
    }

    private static void AppendPluginLoadProgressSummary(
        List<string> retainedWarnings,
        int progressBeginCount,
        int progressEndCount,
        int progressLatestIndex,
        int progressLatestTotal) {
        _ = progressLatestIndex;
        var processed = progressEndCount;
        var total = progressLatestTotal > 0
            ? progressLatestTotal
            : Math.Max(processed, Math.Max(progressBeginCount, progressEndCount));
        processed = Math.Clamp(processed, 0, Math.Max(1, total));
        total = Math.Max(1, total);

        retainedWarnings.Add(StartupBootstrapWarningBuilder.BuildPluginLoadProgressSummary(processed, total, progressBeginCount, progressEndCount));
    }

    private static int ResolvePluginProgressProcessed(int progressEndCount, int progressLatestIndex) {
        _ = progressLatestIndex;
        return Math.Max(0, progressEndCount);
    }

    private static int ResolvePluginProgressTotal(int progressBeginCount, int progressEndCount, int progressLatestTotal, int processed) {
        var total = progressLatestTotal > 0
            ? progressLatestTotal
            : Math.Max(processed, Math.Max(progressBeginCount, progressEndCount));
        return Math.Max(0, total);
    }

    private static void AppendPackLoadProgressSummary(
        List<string> retainedWarnings,
        int progressBeginCount,
        int progressEndCount,
        int progressLatestIndex,
        int progressLatestTotal) {
        _ = progressLatestIndex;
        var processed = progressEndCount;
        var total = progressLatestTotal > 0
            ? progressLatestTotal
            : Math.Max(processed, Math.Max(progressBeginCount, progressEndCount));
        processed = Math.Clamp(processed, 0, Math.Max(1, total));
        total = Math.Max(1, total);

        retainedWarnings.Add(StartupBootstrapWarningBuilder.BuildPackLoadProgressSummary(processed, total, progressBeginCount, progressEndCount));
    }

    private static int ResolvePackProgressProcessed(int progressEndCount, int progressLatestIndex) {
        _ = progressLatestIndex;
        return Math.Max(0, progressEndCount);
    }

    private static int ResolvePackProgressTotal(int progressBeginCount, int progressEndCount, int progressLatestTotal, int processed) {
        var total = progressLatestTotal > 0
            ? progressLatestTotal
            : Math.Max(processed, Math.Max(progressBeginCount, progressEndCount));
        return Math.Max(0, total);
    }

    private static void AppendPackRegistrationProgressSummary(
        List<string> retainedWarnings,
        int progressBeginCount,
        int progressEndCount,
        int progressLatestIndex,
        int progressLatestTotal) {
        _ = progressLatestIndex;
        var processed = progressEndCount;
        var total = progressLatestTotal > 0
            ? progressLatestTotal
            : Math.Max(processed, Math.Max(progressBeginCount, progressEndCount));
        processed = Math.Clamp(processed, 0, Math.Max(1, total));
        total = Math.Max(1, total);

        retainedWarnings.Add(StartupBootstrapWarningBuilder.BuildPackRegistrationProgressSummary(processed, total, progressBeginCount, progressEndCount));
    }

    private static int ResolvePackRegistrationProgressProcessed(int progressEndCount, int progressLatestIndex) {
        _ = progressLatestIndex;
        return Math.Max(0, progressEndCount);
    }

    private static int ResolvePackRegistrationProgressTotal(int progressBeginCount, int progressEndCount, int progressLatestTotal, int processed) {
        var total = progressLatestTotal > 0
            ? progressLatestTotal
            : Math.Max(processed, Math.Max(progressBeginCount, progressEndCount));
        return Math.Max(0, total);
    }

    private static bool TryParseSlowPluginLoadWarning(string warning, out SlowPluginLoadEntry entry) {
        entry = default;
        if (string.IsNullOrWhiteSpace(warning)
            || !warning.StartsWith(PluginLoadTimingWarningPrefix, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (!TryReadQuotedToken(warning, "plugin", out var pluginId)
            || !TryReadQuotedToken(warning, "elapsed_ms", out var elapsedRaw)
            || !long.TryParse(elapsedRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var elapsedMs)
            || elapsedMs <= 0) {
            return false;
        }

        var loaded = TryReadQuotedIntToken(warning, "loaded", out var loadedValue) ? loadedValue : 0;
        var disabled = TryReadQuotedIntToken(warning, "disabled", out var disabledValue) ? disabledValue : 0;
        var failed = TryReadQuotedIntToken(warning, "failed", out var failedValue) ? failedValue : 0;

        entry = new SlowPluginLoadEntry(
            PluginId: pluginId,
            ElapsedMs: elapsedMs,
            Loaded: loaded,
            Disabled: disabled,
            Failed: failed);
        return true;
    }

    private static bool TryReadQuotedIntToken(string warning, string token, out int value) {
        value = 0;
        if (!TryReadQuotedToken(warning, token, out var raw)) {
            return false;
        }
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParsePluginLoadProgressWarning(string warning, out PluginLoadProgressEntry entry) {
        entry = default;
        if (string.IsNullOrWhiteSpace(warning)
            || !warning.StartsWith(PluginLoadProgressWarningPrefix, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (!TryReadQuotedToken(warning, "plugin", out var pluginId)
            || !TryReadQuotedToken(warning, "phase", out var phase)
            || !TryReadQuotedIntToken(warning, "index", out var index)
            || !TryReadQuotedIntToken(warning, "total", out var total)
            || index <= 0
            || total <= 0) {
            return false;
        }

        var isBegin = string.Equals(phase, "begin", StringComparison.OrdinalIgnoreCase);
        var isEnd = string.Equals(phase, "end", StringComparison.OrdinalIgnoreCase);
        if (!isBegin && !isEnd) {
            return false;
        }

        entry = new PluginLoadProgressEntry(
            PluginId: pluginId,
            IsBegin: isBegin,
            Index: index,
            Total: total);
        return true;
    }

    private static bool TryParsePackLoadProgressWarning(string warning, out PackLoadProgressEntry entry) {
        entry = default;
        if (string.IsNullOrWhiteSpace(warning)
            || !warning.StartsWith(PackLoadProgressWarningPrefix, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (!TryReadQuotedToken(warning, "pack", out var packId)
            || !TryReadQuotedToken(warning, "phase", out var phase)
            || !TryReadQuotedIntToken(warning, "index", out var index)
            || !TryReadQuotedIntToken(warning, "total", out var total)
            || index <= 0
            || total <= 0) {
            return false;
        }

        var isBegin = string.Equals(phase, "begin", StringComparison.OrdinalIgnoreCase);
        var isEnd = string.Equals(phase, "end", StringComparison.OrdinalIgnoreCase);
        if (!isBegin && !isEnd) {
            return false;
        }

        var elapsedMs = 0L;
        if (isEnd && (!TryReadQuotedLongToken(warning, "elapsed_ms", out elapsedMs) || elapsedMs < 0)) {
            return false;
        }

        var failed = false;
        if (isEnd && TryReadQuotedIntToken(warning, "failed", out var failedValue)) {
            failed = failedValue != 0;
        }

        entry = new PackLoadProgressEntry(
            PackId: packId,
            IsBegin: isBegin,
            Index: index,
            Total: total,
            ElapsedMs: elapsedMs,
            Failed: failed);
        return true;
    }

    private static bool TryParsePackRegistrationProgressWarning(string warning, out PackRegistrationProgressEntry entry) {
        entry = default;
        if (string.IsNullOrWhiteSpace(warning)
            || !warning.StartsWith(PackRegistrationProgressWarningPrefix, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (!TryReadQuotedToken(warning, "pack", out var packId)
            || !TryReadQuotedToken(warning, "phase", out var phase)
            || !TryReadQuotedIntToken(warning, "index", out var index)
            || !TryReadQuotedIntToken(warning, "total", out var total)
            || index <= 0
            || total <= 0) {
            return false;
        }

        var isBegin = string.Equals(phase, "begin", StringComparison.OrdinalIgnoreCase);
        var isEnd = string.Equals(phase, "end", StringComparison.OrdinalIgnoreCase);
        if (!isBegin && !isEnd) {
            return false;
        }

        var elapsedMs = 0L;
        var toolsRegistered = 0;
        var failed = false;
        if (isEnd) {
            if (!TryReadQuotedLongToken(warning, "elapsed_ms", out elapsedMs) || elapsedMs < 0) {
                return false;
            }

            if (TryReadQuotedIntToken(warning, "tools_registered", out var toolsRegisteredValue)) {
                toolsRegistered = Math.Max(0, toolsRegisteredValue);
            }

            if (TryReadQuotedIntToken(warning, "failed", out var failedValue)) {
                failed = failedValue != 0;
            }
        }

        entry = new PackRegistrationProgressEntry(
            PackId: packId,
            IsBegin: isBegin,
            Index: index,
            Total: total,
            ElapsedMs: elapsedMs,
            ToolsRegistered: toolsRegistered,
            Failed: failed);
        return true;
    }

    private static bool TryReadQuotedToken(string warning, string token, out string value) {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(warning) || string.IsNullOrWhiteSpace(token)) {
            return false;
        }

        var key = token + "='";
        var keyIndex = warning.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (keyIndex < 0) {
            return false;
        }

        var valueStart = keyIndex + key.Length;
        var valueEnd = warning.IndexOf('\'', valueStart);
        if (valueEnd <= valueStart) {
            return false;
        }

        value = warning.Substring(valueStart, valueEnd - valueStart).Trim();
        return value.Length != 0;
    }

    private static bool TryReadQuotedLongToken(string warning, string token, out long value) {
        value = 0;
        if (!TryReadQuotedToken(warning, token, out var raw)) {
            return false;
        }
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private readonly record struct SlowPluginLoadEntry(
        string PluginId,
        long ElapsedMs,
        int Loaded,
        int Disabled,
        int Failed);
    private readonly record struct SlowPackLoadEntry(
        string PackId,
        long ElapsedMs,
        bool Failed);
    private readonly record struct SlowPackRegistrationEntry(
        string PackId,
        long ElapsedMs,
        int ToolsRegistered,
        bool Failed);
    private readonly record struct PluginLoadProgressEntry(
        string PluginId,
        bool IsBegin,
        int Index,
        int Total);
    private readonly record struct PackLoadProgressEntry(
        string PackId,
        bool IsBegin,
        int Index,
        int Total,
        long ElapsedMs,
        bool Failed);
    private readonly record struct PackRegistrationProgressEntry(
        string PackId,
        bool IsBegin,
        int Index,
        int Total,
        long ElapsedMs,
        int ToolsRegistered,
        bool Failed);
    private readonly record struct StartupLoadWarningSummary(
        int SlowPluginCount,
        int SlowPluginTopCount,
        int PluginProgressProcessed,
        int PluginProgressTotal,
        int SlowPackCount,
        int SlowPackTopCount,
        int PackProgressProcessed,
        int PackProgressTotal,
        int SlowPackRegistrationCount,
        int SlowPackRegistrationTopCount,
        int PackRegistrationProgressProcessed,
        int PackRegistrationProgressTotal);
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
