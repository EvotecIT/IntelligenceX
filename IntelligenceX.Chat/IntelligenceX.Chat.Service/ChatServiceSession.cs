using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    private const int MaxTrackedStructuredNextActionContexts = 256;
    private const int MaxTrackedStructuredNextActionReplayGuardContexts = 256;
    private const int MaxTrackedPlannerThreadContexts = 128;
    private const int MaxTrackedDomainIntentFamilyContexts = 256;
    private const int MaxTrackedDomainIntentClarificationContexts = 256;
    private const int MaxTrackedPackPreflightContexts = 256;
    private const int MaxTrackedThreadRecoveryAliases = 256;
    private const int MaxTrackedThreadToolEvidenceContexts = 128;
    private const int MaxToolEvidenceEntriesPerThread = 48;
    private static readonly TimeSpan UserIntentContextMaxAge = TimeSpan.FromHours(6);
    private static readonly TimeSpan PendingActionContextMaxAge = TimeSpan.FromHours(8);
    private static readonly TimeSpan StructuredNextActionContextMaxAge = TimeSpan.FromHours(6);
    private static readonly TimeSpan PlannerThreadContextMaxAge = TimeSpan.FromHours(6);
    private static readonly TimeSpan DomainIntentFamilyContextMaxAge = TimeSpan.FromHours(8);
    private static readonly TimeSpan DomainIntentClarificationContextMaxAge = TimeSpan.FromHours(2);
    private static readonly TimeSpan PackPreflightContextMaxAge = TimeSpan.FromHours(8);
    private static readonly TimeSpan ThreadRecoveryAliasContextMaxAge = TimeSpan.FromHours(12);
    private static readonly TimeSpan ThreadToolEvidenceContextMaxAge = TimeSpan.FromHours(8);
    private static readonly TimeSpan StartupToolHealthPrimeBudget = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan StartupToolHealthHelloWaitBudget = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan NativeUsageRefreshInterval = TimeSpan.FromMinutes(1);
    private readonly ServiceOptions _options;
    private readonly ChatServiceToolingBootstrapCache? _toolingBootstrapCache;
    private readonly Stream _stream;
    private ToolRegistry _registry;
    private IReadOnlyList<IToolPack> _packs;
    private ToolPackAvailabilityInfo[] _packAvailability;
    private ToolPluginAvailabilityInfo[] _pluginAvailability;
    private string[] _connectedRuntimeSkillInventory;
    private bool _connectedRuntimeSkillInventoryHydrated;
    private string[] _startupWarnings;
    private SessionStartupBootstrapTelemetryDto? _startupBootstrap;
    private string[] _pluginSearchPaths;
    private ToolDefinitionDto[] _cachedToolDefinitions;
    private bool _servingPersistedToolingBootstrapPreview;
    private readonly Dictionary<string, string> _packDisplayNamesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _packDescriptionsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolPackSourceKind> _packSourceKindsById = new(StringComparer.OrdinalIgnoreCase);
    private ToolRuntimePolicyDiagnostics _runtimePolicyDiagnostics;
    private ToolRoutingCatalogDiagnostics _routingCatalogDiagnostics;
    private ToolOrchestrationCatalog _toolOrchestrationCatalog;
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
    private readonly Dictionary<string, StructuredNextActionSnapshot> _structuredNextActionByThreadId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StructuredNextActionAutoReplaySnapshot> _structuredNextActionAutoReplayByThreadId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _plannerThreadIdByActiveThreadId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _plannerThreadSeenUtcTicksByActiveThreadId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _domainIntentFamilyByThreadId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _domainIntentFamilySeenUtcTicks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _pendingDomainIntentClarificationSeenUtcTicks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _packPreflightToolNamesByThreadId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _packPreflightSeenUtcTicks = new(StringComparer.Ordinal);
    private readonly object _threadRecoveryAliasLock = new();
    private readonly Dictionary<string, string> _recoveredThreadAliasesByThreadId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _recoveredThreadAliasSeenUtcTicksByThreadId = new(StringComparer.Ordinal);
    private readonly object _threadToolEvidenceLock = new();
    private readonly Dictionary<string, Dictionary<string, ThreadToolEvidenceEntry>> _threadToolEvidenceByThreadId = new(StringComparer.Ordinal);

    private readonly object _modelListCacheLock = new();
    private ModelListCacheEntry? _modelListCache;
    private readonly object _nativeUsageCacheLock = new();
    private string? _nativeUsageCacheAccountId;
    private DateTime _nativeUsageCacheUpdatedUtc;
    private NativeUsageSnapshotDto? _nativeUsageCache;

    private readonly JsonSerializerOptions _json;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private string? _instructions;
    private Task? _startupToolingBootstrapTask;

    private readonly object _loginLock = new();
    private LoginFlow? _login;
    private readonly object _chatRunLock = new();
    private ChatRun? _activeChat;
    private readonly Queue<ChatRun> _queuedChats = new();
    private readonly Dictionary<string, ChatRun> _chatRunsByRequestId = new(StringComparer.Ordinal);
    private Task? _chatRunPumpTask;
    private string? _activeThreadId;
    public ChatServiceSession(ServiceOptions options, Stream stream, ChatServiceToolingBootstrapCache? toolingBootstrapCache = null) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _toolingBootstrapCache = toolingBootstrapCache;
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));

        _runtimePolicyDiagnostics = BuildDefaultRuntimePolicyDiagnostics(_options);
        _registry = new ToolRegistry {
            RequireExplicitRoutingMetadata = _runtimePolicyDiagnostics.RequireExplicitRoutingMetadata
        };
        _packs = Array.Empty<IToolPack>();
        _packAvailability = Array.Empty<ToolPackAvailabilityInfo>();
        _pluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>();
        _connectedRuntimeSkillInventory = Array.Empty<string>();
        _connectedRuntimeSkillInventoryHydrated = false;
        _startupWarnings = Array.Empty<string>();
        _startupBootstrap = null;
        _pluginSearchPaths = Array.Empty<string>();
        _cachedToolDefinitions = Array.Empty<ToolDefinitionDto>();
        _servingPersistedToolingBootstrapPreview = false;
        _routingCatalogDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>());
        _toolOrchestrationCatalog = ToolOrchestrationCatalog.Build(Array.Empty<ToolDefinition>());
        UpdatePackMetadataIndexes(Array.Empty<ToolPackDescriptor>());
        TryApplyPersistedToolingBootstrapPreview();

        _json = new JsonSerializerOptions {
            TypeInfoResolver = ChatServiceJsonContext.Default
        };
        TryRehydrateToolRoutingStats();
    }

    private void UpdatePackMetadataIndexes(IReadOnlyList<ToolPackDescriptor> descriptors) {
        _packDisplayNamesById.Clear();
        _packDescriptionsById.Clear();
        _packSourceKindsById.Clear();
        var descriptorIdsByNormalizedPackId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < descriptors.Count; i++) {
            var descriptor = descriptors[i];
            var descriptorId = (descriptor.Id ?? string.Empty).Trim();
            var normalizedPackId = NormalizePackId(descriptorId);
            if (normalizedPackId.Length == 0) {
                continue;
            }

            if (descriptorIdsByNormalizedPackId.TryGetValue(normalizedPackId, out var existingDescriptorId)
                && !string.Equals(existingDescriptorId, descriptorId, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException(
                    $"Tool pack ids '{existingDescriptorId}' and '{descriptorId}' both normalize to '{normalizedPackId}'.");
            }

            descriptorIdsByNormalizedPackId[normalizedPackId] = descriptorId;
            _packDisplayNamesById[normalizedPackId] = ToolPackMetadataNormalizer.ResolveDisplayName(descriptor.Id, descriptor.Name);
            var description = (descriptor.Description ?? string.Empty).Trim();
            if (description.Length > 0) {
                _packDescriptionsById[normalizedPackId] = description;
            }
            _packSourceKindsById[normalizedPackId] = ToolPackMetadataNormalizer.ResolveSourceKind(descriptor.SourceKind, descriptorId);
        }
    }

    private void UpdatePackMetadataIndexesFromAvailability(IReadOnlyList<ToolPackAvailabilityInfo> packAvailability) {
        _packDisplayNamesById.Clear();
        _packDescriptionsById.Clear();
        _packSourceKindsById.Clear();

        for (var i = 0; i < packAvailability.Count; i++) {
            var pack = packAvailability[i];
            var descriptorId = (pack.Id ?? string.Empty).Trim();
            var normalizedPackId = NormalizePackId(descriptorId);
            if (normalizedPackId.Length == 0) {
                continue;
            }

            _packDisplayNamesById[normalizedPackId] = ToolPackMetadataNormalizer.ResolveDisplayName(pack.Id, pack.Name);
            var description = (pack.Description ?? string.Empty).Trim();
            if (description.Length > 0) {
                _packDescriptionsById[normalizedPackId] = description;
            }

            _packSourceKindsById[normalizedPackId] = ToolPackMetadataNormalizer.ResolveSourceKind(pack.SourceKind, descriptorId);
        }
    }

    internal static bool RequestRequiresConnectedClient(ChatServiceRequest request) {
        return request is ListModelsRequest
               or ChatRequest;
    }

    internal static bool RequestRequiresToolingBootstrap(ChatServiceRequest request) {
        return request is ListToolsRequest
               or CheckToolHealthRequest
               or InvokeToolRequest
               or ChatRequest
               or SetProfileRequest
               or ApplyRuntimeSettingsRequest;
    }

    internal static string BuildToolingBootstrapFailureMessage(ChatServiceRequest request, Exception exception) {
        var detail = (exception?.GetBaseException().Message ?? exception?.Message ?? string.Empty).Trim();
        if (detail.Length == 0) {
            detail = "Tool bootstrap failed.";
        }

        return request switch {
            ListToolsRequest => "Couldn't load tool catalog because tool bootstrap failed: " + detail,
            CheckToolHealthRequest => "Couldn't run tool health probes because tool bootstrap failed: " + detail,
            InvokeToolRequest => "Couldn't invoke tool because tool bootstrap failed: " + detail,
            ChatRequest => "Couldn't start chat because tool bootstrap failed: " + detail,
            SetProfileRequest => "Couldn't apply runtime profile because tool bootstrap failed: " + detail,
            ApplyRuntimeSettingsRequest => "Couldn't apply runtime settings because tool bootstrap failed: " + detail,
            _ => "Couldn't continue because tool bootstrap failed: " + detail
        };
    }

    private static ToolRuntimePolicyDiagnostics BuildDefaultRuntimePolicyDiagnostics(ServiceOptions options) {
        var policyOptions = BuildRuntimePolicyOptions(options);
        var context = ToolRuntimePolicyBootstrap.CreateContext(policyOptions);
        return ToolRuntimePolicyBootstrap.BuildDiagnostics(context);
    }

    internal static string BuildClientConnectFailureMessage(ChatServiceRequest request, Exception exception) {
        var detail = (exception?.Message ?? string.Empty).Trim();
        if (detail.Length == 0) {
            detail = "Runtime provider connection failed.";
        }

        return request switch {
            ListModelsRequest => "Couldn't connect to runtime provider while listing models: " + detail,
            ChatRequest => "Couldn't connect to runtime provider for chat request: " + detail,
            _ => "Couldn't connect to runtime provider: " + detail
        };
    }

    public async Task RunAsync(CancellationToken cancellationToken) {
        var instructions = LoadInstructions(_options);
        _instructions = instructions;
        var startupToolingBootstrapTask = Task.Run(() => RebuildToolingCore(clearRoutingCaches: false), CancellationToken.None);
        _startupToolingBootstrapTask = startupToolingBootstrapTask;
        _ = startupToolingBootstrapTask.ContinueWith(
            faultedBootstrapTask => {
                var detail = (faultedBootstrapTask.Exception?.GetBaseException().Message ?? "Tool bootstrap failed.").Trim();
                if (detail.Length == 0) {
                    detail = "Tool bootstrap failed.";
                }

                RecordStartupWarning("[startup] Tool bootstrap failed: " + detail);
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        _ = RunStartupToolHealthPrimingAfterToolingBootstrapAsync(startupToolingBootstrapTask, cancellationToken);

        using var reader = new StreamReader(_stream, leaveOpen: true);
        using var writer = new StreamWriter(_stream, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        IntelligenceXClient? client = null;

        async Task<IntelligenceXClient> GetOrConnectClientAsync() {
            if (client is not null) {
                return client;
            }

            var transport = ResolveMetricsTransport();
            var connectStopwatch = Stopwatch.StartNew();
            Console.WriteLine(
                $"[startup] provider_connect_progress phase='begin' operation='connect_client' transport='{transport}'");
            try {
                client = await ConnectClientAsync(cancellationToken).ConfigureAwait(false);
                connectStopwatch.Stop();
                var elapsedMs = Math.Max(1L, connectStopwatch.ElapsedMilliseconds);
                Console.WriteLine(
                    $"[startup] provider_connect_progress phase='end' operation='connect_client' transport='{transport}' status='ok' elapsed_ms='{elapsedMs}'");
                return client;
            } catch {
                connectStopwatch.Stop();
                var elapsedMs = Math.Max(1L, connectStopwatch.ElapsedMilliseconds);
                Console.WriteLine(
                    $"[startup] provider_connect_progress phase='end' operation='connect_client' transport='{transport}' status='failed' elapsed_ms='{elapsedMs}'");
                throw;
            }
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

                var activeStartupToolingBootstrapTask = Volatile.Read(ref _startupToolingBootstrapTask) ?? startupToolingBootstrapTask;
                if (RequestRequiresToolingBootstrap(request)) {
                    var shouldBypassToolingBootstrapWait = ShouldBypassToolingBootstrapWait(request, activeStartupToolingBootstrapTask);
                    try {
                        if (!shouldBypassToolingBootstrapWait) {
                            await activeStartupToolingBootstrapTask.ConfigureAwait(false);
                        }
                    } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                        break;
                    } catch (Exception ex) {
                        await WriteAsync(writer, new ErrorMessage {
                            Kind = ChatServiceMessageKind.Response,
                            RequestId = request.RequestId,
                            Error = BuildToolingBootstrapFailureMessage(request, ex),
                            Code = "tooling_bootstrap_failed"
                        }, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                IntelligenceXClient? connectedClient = null;
                if (RequestRequiresConnectedClient(request)) {
                    try {
                        connectedClient = await GetOrConnectClientAsync().ConfigureAwait(false);
                        await RefreshConnectedRuntimeSkillInventoryAsync(connectedClient, cancellationToken).ConfigureAwait(false);
                    } catch (Exception ex) {
                        await WriteAsync(writer, new ErrorMessage {
                            Kind = ChatServiceMessageKind.Response,
                            RequestId = request.RequestId,
                            Error = BuildClientConnectFailureMessage(request, ex),
                            Code = "provider_connect_failed"
                        }, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                switch (request) {
                    case HelloRequest:
                        var helloStartupToolingBootstrapTask = Volatile.Read(ref _startupToolingBootstrapTask) ?? startupToolingBootstrapTask;
                        var helloStartupWarnings = BuildHelloStartupWarnings(helloStartupToolingBootstrapTask);
                        var helloCapabilitySnapshot = BuildRuntimeCapabilitySnapshot();
                        await WriteAsync(writer, new HelloMessage {
                            Kind = ChatServiceMessageKind.Response,
                            RequestId = request.RequestId,
                            Name = "IntelligenceX.Chat.Service",
                            Version = typeof(ChatServiceSession).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                            ProcessId = Environment.ProcessId.ToString(),
                            Policy = BuildSessionPolicy(
                                _options,
                                _packAvailability,
                                _pluginAvailability,
                                helloStartupWarnings,
                                _startupBootstrap,
                                _pluginSearchPaths,
                                _runtimePolicyDiagnostics,
                                _routingCatalogDiagnostics,
                                connectedRuntimeSkills: _connectedRuntimeSkillInventory,
                                healthyToolNames: helloCapabilitySnapshot.HealthyTools,
                                remoteReachabilityMode: helloCapabilitySnapshot.RemoteReachabilityMode,
                                orchestrationCatalog: _toolOrchestrationCatalog,
                                capabilitySnapshot: helloCapabilitySnapshot)
                        }, cancellationToken).ConfigureAwait(false);
                        break;

                    case EnsureLoginRequest login: {
                            IntelligenceXClient? loginClient = null;
                            if (_options.OpenAITransport == OpenAITransportKind.Native) {
                                try {
                                    loginClient = await GetOrConnectClientAsync().ConfigureAwait(false);
                                } catch (Exception ex) {
                                    await WriteAsync(writer, new ErrorMessage {
                                        Kind = ChatServiceMessageKind.Response,
                                        RequestId = login.RequestId,
                                        Error = $"Failed to probe login state: {ex.Message}",
                                        Code = "ensure_login_failed"
                                    }, cancellationToken).ConfigureAwait(false);
                                    break;
                                }
                            }

                            await HandleEnsureLoginAsync(loginClient, writer, login, cancellationToken).ConfigureAwait(false);
                            break;
                        }

                    case StartChatGptLoginRequest startLogin: {
                            IntelligenceXClient? loginClient = null;
                            if (_options.OpenAITransport == OpenAITransportKind.Native) {
                                try {
                                    loginClient = await GetOrConnectClientAsync().ConfigureAwait(false);
                                } catch (Exception ex) {
                                    await WriteAsync(writer, new ErrorMessage {
                                        Kind = ChatServiceMessageKind.Response,
                                        RequestId = startLogin.RequestId,
                                        Error = $"Failed to start ChatGPT login: {ex.Message}",
                                        Code = "login_start_failed"
                                    }, cancellationToken).ConfigureAwait(false);
                                    break;
                                }
                            }

                            await HandleStartChatGptLoginAsync(loginClient, writer, startLogin, cancellationToken).ConfigureAwait(false);
                            break;
                        }

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
                                ResetConnectedRuntimeSkillInventory();
                                ClearActiveThreadId();
                            } else if (setResult.ModelChanged && client is not null) {
                                // Keep the internal thread model selection consistent with the active profile.
                                client.ConfigureDefaults(model: _options.Model);
                            }

                            if (setProfile.NewThread) {
                                ClearActiveThreadId();
                            }

                            break;
                        }

                    case ApplyRuntimeSettingsRequest applyRuntime: {
                            var applyResult = await HandleApplyRuntimeSettingsAsync(writer, applyRuntime, cancellationToken).ConfigureAwait(false);
                            if (applyResult.ReconnectClient) {
                                await DisposeClientAsync(client).ConfigureAwait(false);
                                client = null;
                                ResetConnectedRuntimeSkillInventory();
                                ClearActiveThreadId();
                            } else if (applyResult.ModelChanged && client is not null) {
                                // Keep the internal thread model selection consistent with runtime settings.
                                client.ConfigureDefaults(model: _options.Model);
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
                        await HandleChatRequestAsync(connectedClient!, writer, chat, cancellationToken).ConfigureAwait(false);
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
            _startupToolingBootstrapTask = null;
            await CancelActiveChatIfAnyAsync().ConfigureAwait(false);
            CancelLoginIfActive();
            await DisposeClientAsync(client).ConfigureAwait(false);
            ResetConnectedRuntimeSkillInventory();
        }
    }

    internal static bool ShouldBypassToolingBootstrapWaitForListTools(
        bool isListToolsRequest,
        bool startupToolingBootstrapCompleted,
        bool startupToolingBootstrapCompletedSuccessfully,
        bool hasCachedToolCatalog) {
        if (!isListToolsRequest
            || !startupToolingBootstrapCompleted
            || !startupToolingBootstrapCompletedSuccessfully
            || !hasCachedToolCatalog) {
            return false;
        }

        return true;
    }

    internal static bool ShouldBypassToolingBootstrapWaitForRecoveryRequests(
        bool isRecoveryRequest,
        bool startupToolingBootstrapCompleted,
        bool startupToolingBootstrapCompletedSuccessfully) {
        return isRecoveryRequest
               && startupToolingBootstrapCompleted
               && !startupToolingBootstrapCompletedSuccessfully;
    }

    private bool ShouldBypassToolingBootstrapWait(ChatServiceRequest request, Task startupToolingBootstrapTask) {
        if (ShouldBypassToolingBootstrapWaitForRecoveryRequests(
                isRecoveryRequest: request is SetProfileRequest or ApplyRuntimeSettingsRequest,
                startupToolingBootstrapCompleted: startupToolingBootstrapTask.IsCompleted,
                startupToolingBootstrapCompletedSuccessfully: startupToolingBootstrapTask.IsCompletedSuccessfully)) {
            return true;
        }

        return ShouldBypassToolingBootstrapWaitForListTools(
            isListToolsRequest: request is ListToolsRequest,
            startupToolingBootstrapCompleted: startupToolingBootstrapTask.IsCompleted,
            startupToolingBootstrapCompletedSuccessfully: startupToolingBootstrapTask.IsCompletedSuccessfully,
            hasCachedToolCatalog: TryGetCachedToolCatalogForListTools(out _));
    }

    private void MarkStartupToolingBootstrapRecoveredAfterRuntimeMutation() {
        var startupToolingBootstrapTask = Volatile.Read(ref _startupToolingBootstrapTask);
        if (startupToolingBootstrapTask is null || startupToolingBootstrapTask.IsCompletedSuccessfully) {
            return;
        }

        Volatile.Write(ref _startupToolingBootstrapTask, Task.CompletedTask);
    }

}
