using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int MaxTrackedToolRoutingStats = 512;
    private const int MaxTrackedWeightedRoutingContexts = 256;
    private const int MaxTrackedUserIntentContexts = 256;
    private const int MaxTrackedPendingActionContexts = 256;
    private static readonly TimeSpan UserIntentContextMaxAge = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PendingActionContextMaxAge = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan StartupToolHealthPrimeBudget = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan StartupToolHealthHelloWaitBudget = TimeSpan.FromMilliseconds(250);
    private readonly ServiceOptions _options;
    private readonly Stream _stream;
    private ToolRegistry _registry;
    private IReadOnlyList<IToolPack> _packs;
    private ToolPackAvailabilityInfo[] _packAvailability;
    private string[] _startupWarnings;
    private string[] _pluginSearchPaths;
    private readonly Dictionary<string, string> _packDisplayNamesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _packDescriptionsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolPackSourceKind> _packSourceKindsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toolPackIdsByToolName = new(StringComparer.OrdinalIgnoreCase);
    private ToolRuntimePolicyDiagnostics _runtimePolicyDiagnostics;
    private readonly object _toolRoutingStatsLock = new();
    private readonly Dictionary<string, ToolRoutingStats> _toolRoutingStats = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _toolRoutingContextLock = new();
    private readonly Dictionary<string, string[]> _lastWeightedToolNamesByThreadId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _lastWeightedToolSubsetSeenUtcTicks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lastUserIntentByThreadId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _lastUserIntentSeenUtcTicks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingAction[]> _pendingActionsByThreadId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _pendingActionsSeenUtcTicks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _pendingActionsCallToActionTokensByThreadId = new(StringComparer.Ordinal);

    private readonly object _modelListCacheLock = new();
    private ModelListCacheEntry? _modelListCache;

    private readonly JsonSerializerOptions _json;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private string? _instructions;

    private readonly object _loginLock = new();
    private LoginFlow? _login;
    private readonly object _chatRunLock = new();
    private ChatRun? _activeChat;
    private static readonly Regex UserRequestSectionRegex =
        new(@"\bUser request:\s*(?<value>[\s\S]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public ChatServiceSession(ServiceOptions options, Stream stream) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        var startupWarnings = new List<string>();
        var runtimePolicyContext = ToolRuntimePolicyBootstrap.CreateContext(
            BuildRuntimePolicyOptions(_options),
            warning => RecordBootstrapWarning(startupWarnings, warning));
        var bootstrapOptions = new ToolPackBootstrapOptions {
            AllowedRoots = _options.AllowedRoots.ToArray(),
            AdDomainController = _options.AdDomainController,
            AdDefaultSearchBaseDn = _options.AdDefaultSearchBaseDn,
            AdMaxResults = _options.AdMaxResults,
            EnablePowerShellPack = _options.EnablePowerShellPack,
            PowerShellAllowWrite = _options.PowerShellAllowWrite,
            EnableTestimoXPack = _options.EnableTestimoXPack,
            EnableOfficeImoPack = _options.EnableOfficeImoPack,
            EnableDefaultPluginPaths = _options.EnableDefaultPluginPaths,
            PluginPaths = _options.PluginPaths.ToArray(),
            AuthenticationProbeStore = runtimePolicyContext.AuthenticationProbeStore,
            RequireSuccessfulSmtpProbeForSend = runtimePolicyContext.RequireSuccessfulSmtpProbeForSend,
            SmtpProbeMaxAgeSeconds = runtimePolicyContext.SmtpProbeMaxAgeSeconds,
            RunAsProfilePath = runtimePolicyContext.Options.RunAsProfilePath,
            AuthenticationProfilePath = runtimePolicyContext.Options.AuthenticationProfilePath,
            OnBootstrapWarning = warning => RecordBootstrapWarning(startupWarnings, warning)
        };

        var bootstrapResult = ToolPackBootstrap.CreateDefaultReadOnlyPacksWithAvailability(bootstrapOptions);
        _packs = bootstrapResult.Packs;
        _packAvailability = bootstrapResult.PackAvailability.ToArray();
        _pluginSearchPaths = NormalizeDistinctStrings(ToolPackBootstrap.GetPluginSearchPaths(bootstrapOptions), maxItems: 32);
        _startupWarnings = NormalizeDistinctStrings(startupWarnings, maxItems: 64);
        _registry = new ToolRegistry();
        _toolPackIdsByToolName.Clear();
        ToolPackBootstrap.RegisterAll(_registry, _packs, _toolPackIdsByToolName);
        _runtimePolicyDiagnostics = ToolRuntimePolicyBootstrap.ApplyToRegistry(_registry, runtimePolicyContext);
        UpdatePackMetadataIndexes(ToolPackBootstrap.GetDescriptors(_packs));

        _json = new JsonSerializerOptions {
            TypeInfoResolver = ChatServiceJsonContext.Default
        };
    }

    private void UpdatePackMetadataIndexes(IReadOnlyList<ToolPackDescriptor> descriptors) {
        _packDisplayNamesById.Clear();
        _packDescriptionsById.Clear();
        _packSourceKindsById.Clear();

        for (var i = 0; i < descriptors.Count; i++) {
            var descriptor = descriptors[i];
            var normalizedPackId = NormalizePackId(descriptor.Id);
            if (normalizedPackId.Length == 0) {
                continue;
            }

            _packDisplayNamesById[normalizedPackId] = ResolvePackDisplayName(descriptor.Id, descriptor.Name);
            var description = (descriptor.Description ?? string.Empty).Trim();
            if (description.Length > 0) {
                _packDescriptionsById[normalizedPackId] = description;
            }
            _packSourceKindsById[normalizedPackId] = MapSourceKind(descriptor.SourceKind, descriptor.Id);
        }
    }

    internal static bool RequestRequiresConnectedClient(ChatServiceRequest request) {
        return request is EnsureLoginRequest
               or StartChatGptLoginRequest
               or ListModelsRequest
               or ChatRequest;
    }

    public async Task RunAsync(CancellationToken cancellationToken) {
        var instructions = LoadInstructions(_options);
        _instructions = instructions;
        var startupToolHealthPrimeTask = RunStartupToolHealthPrimingAsync(cancellationToken);

        using var reader = new StreamReader(_stream, leaveOpen: true);
        using var writer = new StreamWriter(_stream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        string? activeThreadId = null;
        IntelligenceXClient? client = null;

        async Task<IntelligenceXClient> GetOrConnectClientAsync() {
            if (client is not null) {
                return client;
            }

            client = await ConnectClientAsync(cancellationToken).ConfigureAwait(false);
            return client;
        }

        try {
            while (!cancellationToken.IsCancellationRequested) {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) {
                    break;
                }
                if (string.IsNullOrWhiteSpace(line)) {
                    continue;
                }

                ChatServiceRequest? request;
                try {
                    request = JsonSerializer.Deserialize(line, ChatServiceJsonContext.Default.ChatServiceRequest);
                } catch (Exception ex) {
                    await WriteAsync(writer, new ErrorMessage {
                        Kind = ChatServiceMessageKind.Response,
                        RequestId = null,
                        Error = $"Invalid request JSON: {ex.Message}",
                        Code = "invalid_json"
                    }, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (request is null) {
                    continue;
                }

                var connectedClient = RequestRequiresConnectedClient(request)
                    ? await GetOrConnectClientAsync().ConfigureAwait(false)
                    : null;

                switch (request) {
                    case HelloRequest:
                        await AwaitStartupToolHealthPrimingForHelloAsync(startupToolHealthPrimeTask, cancellationToken).ConfigureAwait(false);
                        await WriteAsync(writer, new HelloMessage {
                            Kind = ChatServiceMessageKind.Response,
                            RequestId = request.RequestId,
                            Name = "IntelligenceX.Chat.Service",
                            Version = typeof(ChatServiceSession).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                            ProcessId = Environment.ProcessId.ToString(),
                            Policy = BuildSessionPolicy(_options, _packAvailability, _startupWarnings, _pluginSearchPaths, _runtimePolicyDiagnostics)
                        }, cancellationToken).ConfigureAwait(false);
                        break;

                    case EnsureLoginRequest login:
                        await HandleEnsureLoginAsync(connectedClient!, writer, login, cancellationToken).ConfigureAwait(false);
                        break;

                    case StartChatGptLoginRequest startLogin:
                        await HandleStartChatGptLoginAsync(connectedClient!, writer, startLogin, cancellationToken).ConfigureAwait(false);
                        break;

                    case ChatGptLoginPromptResponseRequest promptResponse:
                        await HandleChatGptLoginPromptResponseAsync(writer, promptResponse, cancellationToken).ConfigureAwait(false);
                        break;

                    case CancelChatGptLoginRequest cancelLogin:
                        await HandleCancelChatGptLoginAsync(writer, cancelLogin, cancellationToken).ConfigureAwait(false);
                        break;

                    case ListToolsRequest:
                        await HandleListToolsAsync(writer, request.RequestId, cancellationToken).ConfigureAwait(false);
                        break;

                    case CheckToolHealthRequest checkToolHealth:
                        await HandleToolHealthAsync(writer, checkToolHealth, cancellationToken).ConfigureAwait(false);
                        break;

                    case ListProfilesRequest listProfiles:
                        await HandleListProfilesAsync(writer, listProfiles, cancellationToken).ConfigureAwait(false);
                        break;

                    case SetProfileRequest setProfile: {
                            var setResult = await HandleSetProfileAsync(writer, setProfile, cancellationToken).ConfigureAwait(false);
                            if (setResult.ReconnectClient) {
                                await DisposeClientAsync(client).ConfigureAwait(false);
                                client = null;
                            } else if (setResult.ModelChanged && client is not null) {
                                // Keep the internal thread model selection consistent with the active profile.
                                client.ConfigureDefaults(model: _options.Model);
                            }

                            if (setProfile.NewThread) {
                                activeThreadId = null;
                            }

                            break;
                        }

                    case ListModelsRequest listModels:
                        await HandleListModelsAsync(connectedClient!, writer, listModels, cancellationToken).ConfigureAwait(false);
                        break;

                    case ListModelFavoritesRequest listFavorites:
                        await HandleListModelFavoritesAsync(writer, listFavorites, cancellationToken).ConfigureAwait(false);
                        break;

                    case SetModelFavoriteRequest setFavorite:
                        await HandleSetModelFavoriteAsync(writer, setFavorite, cancellationToken).ConfigureAwait(false);
                        break;

                    case InvokeToolRequest invokeTool:
                        await HandleInvokeToolAsync(writer, invokeTool, cancellationToken).ConfigureAwait(false);
                        break;

                    case CancelChatRequest cancelChat:
                        await HandleCancelChatAsync(writer, cancelChat, cancellationToken).ConfigureAwait(false);
                        break;

                    case ChatRequest chat:
                        activeThreadId = await HandleChatRequestAsync(connectedClient!, writer, chat, activeThreadId, cancellationToken).ConfigureAwait(false);
                        break;

                    default:
                        await WriteAsync(writer, new ErrorMessage {
                            Kind = ChatServiceMessageKind.Response,
                            RequestId = request.RequestId,
                            Error = $"Unsupported request type: {request.GetType().Name}",
                            Code = "unsupported"
                        }, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
        } finally {
            await CancelActiveChatIfAnyAsync().ConfigureAwait(false);
            CancelLoginIfActive();
            await DisposeClientAsync(client).ConfigureAwait(false);
        }
    }

}
