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
    private const int MaxTrackedThreadBackgroundWorkContexts = 256;
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
    private static readonly TimeSpan ThreadBackgroundWorkContextMaxAge = TimeSpan.FromHours(8);
    private static readonly TimeSpan StartupToolHealthPrimeBudget = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan StartupToolHealthHelloWaitBudget = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan NativeUsageRefreshInterval = TimeSpan.FromMinutes(1);
    private readonly ServiceOptions _options;
    private readonly ChatServiceToolingBootstrapCache? _toolingBootstrapCache;
    private readonly ChatServiceBackgroundSchedulerControlState _backgroundSchedulerControlState;
    private readonly Stream _stream;
    private ToolRegistry _registry;
    private IReadOnlyList<IToolPack> _packs;
    private ToolPackAvailabilityInfo[] _packAvailability;
    private ToolPluginAvailabilityInfo[] _pluginAvailability;
    private ToolPluginCatalogInfo[] _pluginCatalog;
    private string[] _connectedRuntimeSkillInventory;
    private bool _connectedRuntimeSkillInventoryHydrated;
    private string[] _startupWarnings;
    private SessionStartupBootstrapTelemetryDto? _startupBootstrap;
    private string[] _pluginSearchPaths;
    private ToolDefinitionDto[] _cachedToolDefinitions;
    private ToolDefinitionDto[] _deferredDescriptorPreviewToolDefinitions;
    private bool _servingPersistedToolingBootstrapPreview;
    private ToolPackInfoDto[] _persistedPreviewPackSummaries;
    private SessionCapabilitySnapshotDto? _persistedPreviewCapabilitySnapshot;
    private SessionCapabilitySnapshotDto? _deferredDescriptorPreviewCapabilitySnapshot;
    private int _deferredDescriptorPreviewCapabilitySnapshotBuildInProgress;
    private readonly Dictionary<string, string> _packDisplayNamesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _packDescriptionsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolPackSourceKind> _packSourceKindsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _packEngineIdsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _packAliasesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _packIdsByAlias = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _packCategoriesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _packCapabilityTagsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _packSearchTokensById = new(StringComparer.OrdinalIgnoreCase);
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
    private readonly object _threadBackgroundWorkLock = new();
    private readonly Dictionary<string, ThreadBackgroundWorkSnapshot> _threadBackgroundWorkByThreadId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _threadBackgroundWorkSeenUtcTicksByThreadId = new(StringComparer.Ordinal);
    private long _backgroundSchedulerLastTickUtcTicks;
    private readonly object _backgroundSchedulerTelemetryLock = new();
    private string _backgroundSchedulerLastOutcome = string.Empty;
    private long _backgroundSchedulerLastOutcomeUtcTicks;
    private long _backgroundSchedulerLastSuccessUtcTicks;
    private long _backgroundSchedulerLastFailureUtcTicks;
    private int _backgroundSchedulerCompletedExecutionCount;
    private int _backgroundSchedulerRequeuedExecutionCount;
    private int _backgroundSchedulerReleasedExecutionCount;
    private int _backgroundSchedulerConsecutiveFailureCount;
    private long _backgroundSchedulerPausedUntilUtcTicks;
    private string _backgroundSchedulerPauseReason = string.Empty;
    private long _backgroundSchedulerLastAdaptiveIdleUtcTicks;
    private int _backgroundSchedulerLastAdaptiveIdleDelaySeconds;
    private string _backgroundSchedulerLastAdaptiveIdleReason = string.Empty;
    private readonly List<SessionCapabilityBackgroundSchedulerActivityDto> _backgroundSchedulerRecentActivity = new();

    private readonly object _modelListCacheLock = new();
    private ModelListCacheEntry? _modelListCache;
    private readonly object _nativeUsageCacheLock = new();
    private string? _nativeUsageCacheAccountId;
    private DateTime _nativeUsageCacheUpdatedUtc;
    private NativeUsageSnapshotDto? _nativeUsageCache;

    private readonly JsonSerializerOptions _json;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private string? _instructions;
    private readonly object _startupToolingBootstrapLock = new();
    private Task? _startupToolingBootstrapTask;

    private readonly object _loginLock = new();
    private LoginFlow? _login;
    private readonly object _chatRunLock = new();
    private ChatRun? _activeChat;
    private readonly Queue<ChatRun> _queuedChats = new();
    private readonly Dictionary<string, ChatRun> _chatRunsByRequestId = new(StringComparer.Ordinal);
    private Task? _chatRunPumpTask;
    private string? _activeThreadId;
    public ChatServiceSession(
        ServiceOptions options,
        Stream stream,
        ChatServiceToolingBootstrapCache? toolingBootstrapCache = null,
        ChatServiceBackgroundSchedulerControlState? backgroundSchedulerControlState = null) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _toolingBootstrapCache = toolingBootstrapCache;
        _backgroundSchedulerControlState = backgroundSchedulerControlState ?? new ChatServiceBackgroundSchedulerControlState(_options);
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));

        _runtimePolicyDiagnostics = BuildDefaultRuntimePolicyDiagnostics(_options);
        _registry = new ToolRegistry {
            RequireExplicitRoutingMetadata = _runtimePolicyDiagnostics.RequireExplicitRoutingMetadata
        };
        _packs = Array.Empty<IToolPack>();
        _packAvailability = Array.Empty<ToolPackAvailabilityInfo>();
        _pluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>();
        _pluginCatalog = Array.Empty<ToolPluginCatalogInfo>();
        _connectedRuntimeSkillInventory = Array.Empty<string>();
        _connectedRuntimeSkillInventoryHydrated = false;
        _startupWarnings = Array.Empty<string>();
        _startupBootstrap = null;
        _pluginSearchPaths = Array.Empty<string>();
        _cachedToolDefinitions = Array.Empty<ToolDefinitionDto>();
        _deferredDescriptorPreviewToolDefinitions = Array.Empty<ToolDefinitionDto>();
        _servingPersistedToolingBootstrapPreview = false;
        _persistedPreviewPackSummaries = Array.Empty<ToolPackInfoDto>();
        _persistedPreviewCapabilitySnapshot = null;
        _deferredDescriptorPreviewCapabilitySnapshot = null;
        _routingCatalogDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>());
        _toolOrchestrationCatalog = ToolOrchestrationCatalog.Build(Array.Empty<ToolDefinition>());
        UpdatePackMetadataIndexes(Array.Empty<ToolPackDescriptor>());
        TryApplyPersistedToolingBootstrapPreview();
        if (_startupWarnings.Length == 0
            && _toolingBootstrapCache is not null
            && _toolingBootstrapCache.TryGetPersistedSnapshotLoadWarning(out var persistedSnapshotLoadWarning)) {
            _startupWarnings = NormalizeDistinctStrings(new[] { persistedSnapshotLoadWarning }, maxItems: 64);
        }

        _json = new JsonSerializerOptions {
            TypeInfoResolver = ChatServiceJsonContext.Default
        };
        TryRehydrateToolRoutingStats();
        TryRehydrateBackgroundSchedulerRuntimeState();
    }

    private void UpdatePackMetadataIndexes(IReadOnlyList<ToolPackDescriptor> descriptors) {
        _packDisplayNamesById.Clear();
        _packDescriptionsById.Clear();
        _packSourceKindsById.Clear();
        _packEngineIdsById.Clear();
        _packAliasesById.Clear();
        _packIdsByAlias.Clear();
        _packCategoriesById.Clear();
        _packCapabilityTagsById.Clear();
        _packSearchTokensById.Clear();
        var descriptorIdsByNormalizedPackId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < descriptors.Count; i++) {
            var descriptor = descriptors[i];
            var descriptorId = (descriptor.Id ?? string.Empty).Trim();
            var normalizedPackId = NormalizePackId(descriptorId);
            if (normalizedPackId.Length == 0) {
                continue;
            }

            EnsureNoPackIdentityNormalizationCollisions(descriptorIdsByNormalizedPackId, descriptor);
            _packDisplayNamesById[normalizedPackId] = ToolPackMetadataNormalizer.ResolveDisplayName(descriptor.Id, descriptor.Name);
            var description = (descriptor.Description ?? string.Empty).Trim();
            if (description.Length > 0) {
                _packDescriptionsById[normalizedPackId] = description;
            }
            _packSourceKindsById[normalizedPackId] = ToolPackMetadataNormalizer.ResolveSourceKind(descriptor.SourceKind);
            var engineId = ToolPackMetadataNormalizer.NormalizeDescriptorToken(descriptor.EngineId);
            if (engineId.Length > 0) {
                _packEngineIdsById[normalizedPackId] = engineId;
            }
            RememberPackAliases(normalizedPackId, descriptor.Aliases);

            var category = ToolPackBootstrap.NormalizePackCategory(descriptor.Category, descriptor.Id);
            if (!string.IsNullOrWhiteSpace(category)) {
                _packCategoriesById[normalizedPackId] = category;
            }

            var capabilityTags = NormalizeDescriptorTokens(descriptor.CapabilityTags);
            if (capabilityTags.Length > 0) {
                _packCapabilityTagsById[normalizedPackId] = capabilityTags;
            }

            var searchTokens = ToolPackBootstrap.NormalizePackSearchTokens(
                packId: normalizedPackId,
                aliases: _packAliasesById.TryGetValue(normalizedPackId, out var packAliases) ? packAliases : null,
                category: category,
                engineId: engineId,
                explicitSearchTokens: descriptor.SearchTokens);
            if (searchTokens.Length > 0) {
                _packSearchTokensById[normalizedPackId] = searchTokens;
            }
        }
    }

    private void UpdatePackMetadataIndexesFromAvailability(IReadOnlyList<ToolPackAvailabilityInfo> packAvailability) {
        _packDisplayNamesById.Clear();
        _packDescriptionsById.Clear();
        _packSourceKindsById.Clear();
        _packEngineIdsById.Clear();
        _packAliasesById.Clear();
        _packIdsByAlias.Clear();
        _packCategoriesById.Clear();
        _packCapabilityTagsById.Clear();
        _packSearchTokensById.Clear();

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

            _packSourceKindsById[normalizedPackId] = ToolPackMetadataNormalizer.ResolveSourceKind(pack.SourceKind);
            var engineId = ToolPackMetadataNormalizer.NormalizeDescriptorToken(pack.EngineId);
            if (engineId.Length > 0) {
                _packEngineIdsById[normalizedPackId] = engineId;
            }
            RememberPackAliases(normalizedPackId, pack.Aliases);

            var category = ToolPackBootstrap.NormalizePackCategory(pack.Category, normalizedPackId);
            if (!string.IsNullOrWhiteSpace(category)) {
                _packCategoriesById[normalizedPackId] = category;
            }

            var capabilityTags = NormalizeDescriptorTokens(pack.CapabilityTags);
            if (capabilityTags.Length > 0) {
                _packCapabilityTagsById[normalizedPackId] = capabilityTags;
            }

            var searchTokens = ToolPackBootstrap.NormalizePackSearchTokens(
                packId: normalizedPackId,
                aliases: _packAliasesById.TryGetValue(normalizedPackId, out var packAliases) ? packAliases : null,
                category: category,
                engineId: engineId,
                explicitSearchTokens: pack.SearchTokens);
            if (searchTokens.Length > 0) {
                _packSearchTokensById[normalizedPackId] = searchTokens;
            }
        }
    }

    private void RememberPackAliases(string normalizedPackId, IReadOnlyList<string>? aliases) {
        var canonicalPackId = NormalizePackId(normalizedPackId);
        if (canonicalPackId.Length == 0) {
            return;
        }

        RememberPackAliasToken(canonicalPackId, canonicalPackId);
        var normalizedAliases = ToolPackBootstrap.NormalizePackAliases(canonicalPackId, aliases);
        if (normalizedAliases.Length == 0) {
            _packAliasesById.Remove(canonicalPackId);
            return;
        }

        _packAliasesById[canonicalPackId] = normalizedAliases;
        for (var i = 0; i < normalizedAliases.Length; i++) {
            RememberPackAliasToken(canonicalPackId, normalizedAliases[i]);
        }
    }

    private void RememberPackAliasToken(string normalizedPackId, string? alias) {
        var normalizedAlias = ToolPackMetadataNormalizer.NormalizeDescriptorToken(alias);
        if (normalizedPackId.Length == 0 || normalizedAlias.Length == 0) {
            return;
        }

        _packIdsByAlias[normalizedAlias] = normalizedPackId;
    }

    private string ResolveRuntimePackId(string? packId) {
        var normalizedPackId = NormalizePackId(packId);
        if (normalizedPackId.Length > 0) {
            var normalizedAlias = ToolPackMetadataNormalizer.NormalizeDescriptorToken(packId);
            if (normalizedAlias.Length > 0
                && _packIdsByAlias.TryGetValue(normalizedAlias, out var aliasPackId)
                && aliasPackId.Length > 0) {
                return aliasPackId;
            }

            return normalizedPackId;
        }

        var fallbackAlias = ToolPackMetadataNormalizer.NormalizeDescriptorToken(packId);
        return fallbackAlias.Length > 0 && _packIdsByAlias.TryGetValue(fallbackAlias, out var resolvedPackId)
            ? resolvedPackId
            : string.Empty;
    }

    private static string[] NormalizeDescriptorTokens(IReadOnlyList<string>? values) {
        if (values is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(values.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < values.Count; i++) {
            var token = ToolPackMetadataNormalizer.NormalizeDescriptorToken(values[i]);
            if (token.Length == 0 || !seen.Add(token)) {
                continue;
            }

            normalized.Add(token);
        }

        normalized.Sort(StringComparer.OrdinalIgnoreCase);
        return normalized.Count == 0 ? Array.Empty<string>() : normalized.ToArray();
    }

    private static void EnsureNoPackIdentityNormalizationCollisions(
        IDictionary<string, string> descriptorIdsByNormalizedPackId,
        ToolPackDescriptor descriptor) {
        var descriptorId = (descriptor.Id ?? string.Empty).Trim();
        var normalizedPrimaryPackId = NormalizePackId(descriptorId);
        EnsureNoPackIdNormalizationCollision(descriptorIdsByNormalizedPackId, descriptorId, normalizedPrimaryPackId);

        if (descriptor.Aliases is not { Count: > 0 }) {
            return;
        }

        var seenAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < descriptor.Aliases.Count; i++) {
            var alias = (descriptor.Aliases[i] ?? string.Empty).Trim();
            if (alias.Length == 0 || !seenAliases.Add(alias)) {
                continue;
            }

            var normalizedAliasToken = ToolPackMetadataNormalizer.NormalizeDescriptorToken(alias);
            if (normalizedAliasToken.Length == 0) {
                continue;
            }

            var knownAliasPackId = NormalizePackId(alias);
            var aliasMapsKnownIdentity = ToolPackIdentityCatalog.IsKnownPackIdentityToken(alias);
            if (normalizedPrimaryPackId.Length > 0
                && aliasMapsKnownIdentity
                && !string.Equals(knownAliasPackId, normalizedPrimaryPackId, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException(
                    $"Tool pack alias '{alias}' for '{NormalizeCollisionDescriptorId(descriptorId)}' resolves to known pack '{knownAliasPackId}' instead of '{normalizedPrimaryPackId}'.");
            }

            EnsureNoPackIdNormalizationCollision(descriptorIdsByNormalizedPackId, descriptorId, normalizedAliasToken);
        }
    }

    private static void EnsureNoPackIdNormalizationCollision(
        IDictionary<string, string> descriptorIdsByNormalizedPackId,
        string descriptorId,
        string normalizedPackId) {
        if (normalizedPackId.Length == 0) {
            return;
        }

        var normalizedDescriptorId = NormalizeCollisionDescriptorId(descriptorId);
        if (descriptorIdsByNormalizedPackId.TryGetValue(normalizedPackId, out var existingDescriptorId)
            && !string.Equals(existingDescriptorId, normalizedDescriptorId, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"Tool pack ids '{existingDescriptorId}' and '{normalizedDescriptorId}' both normalize to '{normalizedPackId}'.");
        }

        descriptorIdsByNormalizedPackId[normalizedPackId] = normalizedDescriptorId;
    }

    private static string NormalizeCollisionDescriptorId(string descriptorId) {
        var normalized = (descriptorId ?? string.Empty).Trim();
        return normalized.Length == 0 ? "<empty>" : normalized;
    }

    internal static bool RequestRequiresConnectedClient(ChatServiceRequest request) {
        return request is ListModelsRequest
               or ChatRequest;
    }

    internal static bool RequestRequiresToolingBootstrap(ChatServiceRequest request) {
        return request is ListToolsRequest
               or GetBackgroundSchedulerStatusRequest
               or SetBackgroundSchedulerStateRequest
               or SetBackgroundSchedulerMaintenanceWindowsRequest
               or SetBackgroundSchedulerBlockedPacksRequest
               or SetBackgroundSchedulerBlockedThreadsRequest
               or CheckToolHealthRequest
               or ChatRequest
               or SetProfileRequest
               or ApplyRuntimeSettingsRequest;
    }

    internal static bool ShouldOverlapClientConnectWithToolingBootstrap(
        bool requestRequiresConnectedClient,
        bool requestRequiresToolingBootstrap) {
        return requestRequiresConnectedClient && requestRequiresToolingBootstrap;
    }

    internal static string BuildToolingBootstrapFailureMessage(ChatServiceRequest request, Exception exception) {
        var detail = (exception?.GetBaseException().Message ?? exception?.Message ?? string.Empty).Trim();
        if (detail.Length == 0) {
            detail = "Tool bootstrap failed.";
        }

        return request switch {
            ListToolsRequest => "Couldn't load tool catalog because tool bootstrap failed: " + detail,
            GetBackgroundSchedulerStatusRequest => "Couldn't load background scheduler status because tool bootstrap failed: " + detail,
            SetBackgroundSchedulerStateRequest => "Couldn't update background scheduler state because tool bootstrap failed: " + detail,
            SetBackgroundSchedulerMaintenanceWindowsRequest => "Couldn't update background scheduler maintenance windows because tool bootstrap failed: " + detail,
            SetBackgroundSchedulerBlockedPacksRequest => "Couldn't update background scheduler blocked-pack policy because tool bootstrap failed: " + detail,
            SetBackgroundSchedulerBlockedThreadsRequest => "Couldn't update background scheduler blocked-thread policy because tool bootstrap failed: " + detail,
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

    private Task EnsureStartupToolingBootstrapTaskStarted(CancellationToken cancellationToken) {
        var existingTask = Volatile.Read(ref _startupToolingBootstrapTask);
        if (existingTask is not null) {
            return existingTask;
        }

        lock (_startupToolingBootstrapLock) {
            existingTask = _startupToolingBootstrapTask;
            if (existingTask is not null) {
                return existingTask;
            }

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
            return startupToolingBootstrapTask;
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken) {
        var instructions = LoadInstructions(_options);
        _instructions = instructions;

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

                var deferredChatToolingPrepared = false;
                if (request is ChatRequest deferredChatRequest) {
                    try {
                        deferredChatToolingPrepared = await TryPrepareDeferredChatToolingForRequestAsync(
                                writer,
                                deferredChatRequest.RequestId,
                                deferredChatRequest,
                                cancellationToken)
                            .ConfigureAwait(false);
                    } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                        break;
                    }
                }

                var requestRequiresConnectedClient = RequestRequiresConnectedClient(request);
                var requestRequiresToolingBootstrap = RequestRequiresToolingBootstrap(request) && !deferredChatToolingPrepared;
                Task<IntelligenceXClient>? connectedClientTask = null;
                if (ShouldOverlapClientConnectWithToolingBootstrap(
                        requestRequiresConnectedClient,
                        requestRequiresToolingBootstrap)) {
                    connectedClientTask = GetOrConnectClientAsync();
                    _ = connectedClientTask.ContinueWith(
                        static faultedConnectTask => _ = faultedConnectTask.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }

                if (requestRequiresToolingBootstrap) {
                    var activeStartupToolingBootstrapTask = EnsureStartupToolingBootstrapTaskStarted(cancellationToken);
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
                if (requestRequiresConnectedClient) {
                    try {
                        connectedClient = connectedClientTask is null
                            ? await GetOrConnectClientAsync().ConfigureAwait(false)
                            : await connectedClientTask.ConfigureAwait(false);
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
                        var helloStartupToolingBootstrapTask = Volatile.Read(ref _startupToolingBootstrapTask);
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
                                capabilitySnapshot: helloCapabilitySnapshot,
                                pluginCatalog: _pluginCatalog)
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

                    case GetBackgroundSchedulerStatusRequest getBackgroundSchedulerStatus:
                        await HandleBackgroundSchedulerStatusAsync(writer, getBackgroundSchedulerStatus, cancellationToken).ConfigureAwait(false);
                        break;

                    case SetBackgroundSchedulerStateRequest setBackgroundSchedulerState:
                        await HandleBackgroundSchedulerStateAsync(writer, setBackgroundSchedulerState, cancellationToken).ConfigureAwait(false);
                        break;

                    case SetBackgroundSchedulerMaintenanceWindowsRequest setBackgroundSchedulerMaintenanceWindows:
                        await HandleBackgroundSchedulerMaintenanceWindowsAsync(writer, setBackgroundSchedulerMaintenanceWindows, cancellationToken).ConfigureAwait(false);
                        break;

                    case SetBackgroundSchedulerBlockedPacksRequest setBackgroundSchedulerBlockedPacks:
                        await HandleBackgroundSchedulerBlockedPacksAsync(writer, setBackgroundSchedulerBlockedPacks, cancellationToken).ConfigureAwait(false);
                        break;

                    case SetBackgroundSchedulerBlockedThreadsRequest setBackgroundSchedulerBlockedThreads:
                        await HandleBackgroundSchedulerBlockedThreadsAsync(writer, setBackgroundSchedulerBlockedThreads, cancellationToken).ConfigureAwait(false);
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
        if (!isListToolsRequest || !hasCachedToolCatalog) {
            return false;
        }

        if (!startupToolingBootstrapCompleted) {
            return true;
        }

        return startupToolingBootstrapCompletedSuccessfully;
    }

    internal static bool ShouldBypassToolingBootstrapWaitForRecoveryRequests(
        bool isRecoveryRequest,
        bool startupToolingBootstrapCompleted,
        bool startupToolingBootstrapCompletedSuccessfully) {
        return isRecoveryRequest
               && startupToolingBootstrapCompleted
               && !startupToolingBootstrapCompletedSuccessfully;
    }

    internal static bool ShouldBypassToolingBootstrapWaitForChatRequests(
        bool isChatRequest,
        bool startupToolingBootstrapCompleted,
        bool startupToolingBootstrapCompletedSuccessfully,
        bool hasExplicitToolEnableSelectors,
        bool hasDeferredToolCandidateMatch,
        bool executionContractApplies,
        bool continuationContractDetected,
        bool hasPendingActionContext,
        bool hasToolActivity) {
        if (!isChatRequest) {
            return false;
        }

        if (hasExplicitToolEnableSelectors
            || hasDeferredToolCandidateMatch
            || executionContractApplies
            || continuationContractDetected
            || hasPendingActionContext
            || hasToolActivity) {
            return false;
        }

        if (!startupToolingBootstrapCompleted) {
            return true;
        }

        return !startupToolingBootstrapCompletedSuccessfully;
    }

    private bool ShouldBypassToolingBootstrapWait(ChatServiceRequest request, Task startupToolingBootstrapTask) {
        if (ShouldBypassToolingBootstrapWaitForRecoveryRequests(
                isRecoveryRequest: request is SetProfileRequest or ApplyRuntimeSettingsRequest,
                startupToolingBootstrapCompleted: startupToolingBootstrapTask.IsCompleted,
                startupToolingBootstrapCompletedSuccessfully: startupToolingBootstrapTask.IsCompletedSuccessfully)) {
            return true;
        }

        if (request is ChatRequest chatRequest) {
            var normalizedThreadId = (chatRequest.ThreadId ?? string.Empty).Trim();
            var requestText = chatRequest.Text ?? string.Empty;
            var hasPendingActionContext = normalizedThreadId.Length > 0 && HasFreshPendingActionsContext(normalizedThreadId);
            var hasToolActivity = normalizedThreadId.Length > 0 && HasFreshThreadToolEvidence(normalizedThreadId);
            var continuationContractDetected = TryReadContinuationContractFromRequestText(requestText, out _, out _);
            var executionContractApplies = ShouldEnforceExecuteOrExplainContract(requestText);
            var hasExplicitToolEnableSelectors = HasExplicitToolEnableSelectors(chatRequest.Options);
            var hasDeferredToolCandidateMatch = HasDeferredToolCandidateMatchForChatRequest(requestText, chatRequest.Options);
            if (ShouldBypassToolingBootstrapWaitForChatRequests(
                    isChatRequest: true,
                    startupToolingBootstrapCompleted: startupToolingBootstrapTask.IsCompleted,
                    startupToolingBootstrapCompletedSuccessfully: startupToolingBootstrapTask.IsCompletedSuccessfully,
                    hasExplicitToolEnableSelectors: hasExplicitToolEnableSelectors,
                    hasDeferredToolCandidateMatch: hasDeferredToolCandidateMatch,
                    executionContractApplies: executionContractApplies,
                    continuationContractDetected: continuationContractDetected,
                    hasPendingActionContext: hasPendingActionContext,
                    hasToolActivity: hasToolActivity)) {
                return true;
            }
        }

        return ShouldBypassToolingBootstrapWaitForListTools(
            isListToolsRequest: request is ListToolsRequest,
            startupToolingBootstrapCompleted: startupToolingBootstrapTask.IsCompleted,
            startupToolingBootstrapCompletedSuccessfully: startupToolingBootstrapTask.IsCompletedSuccessfully,
            hasCachedToolCatalog: TryGetCachedToolCatalogForListTools(out _));
    }

    private static bool HasExplicitToolEnableSelectors(ChatRequestOptions? options) {
        if (options is null) {
            return false;
        }

        return HasNonEmptySelectorValues(options.EnabledTools)
               || HasNonEmptySelectorValues(options.EnabledPackIds);
    }

    private static bool HasNonEmptySelectorValues(string[]? values) {
        if (values is not { Length: > 0 }) {
            return false;
        }

        for (var i = 0; i < values.Length; i++) {
            if (!string.IsNullOrWhiteSpace(values[i])) {
                return true;
            }
        }

        return false;
    }

    private bool HasDeferredToolCandidateMatchForChatRequest(string requestText, ChatRequestOptions? options) {
        return ResolveDeferredToolPreferenceHints(
                requestText,
                options,
                maxPreferredPackIds: 1,
                maxPreferredToolNames: 1)
            .HasAnyMatches;
    }

    private ToolDefinitionDto[] GetDeferredToolDefinitionsForBootstrapDecision(ChatRequestOptions? options) {
        var merged = new Dictionary<string, ToolDefinitionDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in Volatile.Read(ref _cachedToolDefinitions)) {
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name)) {
                continue;
            }

            merged[definition.Name.Trim()] = definition;
        }

        if (_deferredDescriptorPreviewToolDefinitions.Length == 0) {
            _ = TryGetDeferredDescriptorPreviewCapabilitySnapshot(out _);
        }

        foreach (var definition in _deferredDescriptorPreviewToolDefinitions) {
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name)) {
                continue;
            }

            merged[definition.Name.Trim()] = definition;
        }

        if (merged.Count == 0) {
            return Array.Empty<ToolDefinitionDto>();
        }

        return ApplyDeferredToolExposureOverrides(merged.Values.ToArray(), options);
    }

    private static ToolDefinitionDto[] ApplyDeferredToolExposureOverrides(
        IReadOnlyList<ToolDefinitionDto> definitions,
        ChatRequestOptions? options) {
        if (definitions.Count == 0 || options is null) {
            return definitions is ToolDefinitionDto[] array ? array : definitions.ToArray();
        }

        var enabledToolNames = BuildSelectorSet(options.EnabledTools);
        var enabledPackIds = BuildSelectorSet(options.EnabledPackIds);
        var disabledToolNames = BuildSelectorSet(options.DisabledTools);
        var disabledPackIds = BuildSelectorSet(options.DisabledPackIds);
        var filtered = new List<ToolDefinitionDto>(definitions.Count);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            var toolName = (definition.Name ?? string.Empty).Trim();
            var packId = ToolPackBootstrap.NormalizePackId(definition.PackId);
            if (enabledToolNames is not null && !enabledToolNames.Contains(toolName)) {
                continue;
            }

            if (enabledPackIds is not null && !enabledPackIds.Contains(packId)) {
                continue;
            }

            if (disabledToolNames is not null && disabledToolNames.Contains(toolName)) {
                continue;
            }

            if (disabledPackIds is not null && disabledPackIds.Contains(packId)) {
                continue;
            }

            filtered.Add(definition);
        }

        return filtered.ToArray();
    }

    private static HashSet<string>? BuildSelectorSet(string[]? values) {
        if (values is not { Length: > 0 }) {
            return null;
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < values.Length; i++) {
            var value = (values[i] ?? string.Empty).Trim();
            if (value.Length == 0) {
                continue;
            }

            normalized.Add(value);
            var normalizedPackId = ToolPackBootstrap.NormalizePackId(value);
            if (normalizedPackId.Length > 0) {
                normalized.Add(normalizedPackId);
            }
        }

        return normalized.Count == 0 ? null : normalized;
    }

    private static int CountSupportedDeferredToolTokenHits(
        IReadOnlyList<string> allSearchTexts,
        string searchText,
        IReadOnlyList<string> tokens,
        int maxTokenSupport) {
        if (tokens.Count == 0 || searchText.Length == 0) {
            return 0;
        }

        var hits = 0;
        for (var t = 0; t < tokens.Count; t++) {
            var token = tokens[t];
            if (token.Length == 0) {
                continue;
            }

            var support = 0;
            for (var i = 0; i < allSearchTexts.Count; i++) {
                if (allSearchTexts[i].IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                    support++;
                }
            }

            if (support <= maxTokenSupport
                && searchText.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                hits++;
            }
        }

        return hits;
    }

    private static string BuildDeferredToolRoutingSearchText(ToolDefinitionDto definition) {
        var parts = new List<string>(16) {
            definition.Name,
            definition.Description
        };

        if (!string.IsNullOrWhiteSpace(definition.DisplayName)) {
            parts.Add(definition.DisplayName);
        }

        if (!string.IsNullOrWhiteSpace(definition.Category)) {
            parts.Add(definition.Category);
        }

        if (!string.IsNullOrWhiteSpace(definition.PackId)) {
            parts.Add(definition.PackId);
        }

        if (!string.IsNullOrWhiteSpace(definition.PackName)) {
            parts.Add(definition.PackName);
        }

        if (!string.IsNullOrWhiteSpace(definition.PackDescription)) {
            parts.Add(definition.PackDescription);
        }

        if (!string.IsNullOrWhiteSpace(definition.RoutingRole)) {
            parts.Add(definition.RoutingRole);
        }

        if (!string.IsNullOrWhiteSpace(definition.RoutingScope)) {
            parts.Add(definition.RoutingScope);
        }

        if (!string.IsNullOrWhiteSpace(definition.RoutingOperation)) {
            parts.Add(definition.RoutingOperation);
        }

        if (!string.IsNullOrWhiteSpace(definition.RoutingEntity)) {
            parts.Add(definition.RoutingEntity);
        }

        if (!string.IsNullOrWhiteSpace(definition.DomainIntentFamily)) {
            parts.Add(definition.DomainIntentFamily);
        }

        if (!string.IsNullOrWhiteSpace(definition.ExecutionScope)) {
            parts.Add(definition.ExecutionScope);
        }

        parts.AddRange(definition.Tags ?? Array.Empty<string>());
        parts.AddRange(definition.RepresentativeExamples ?? Array.Empty<string>());
        return string.Join(' ', parts.Where(static value => !string.IsNullOrWhiteSpace(value)));
    }

    private void MarkStartupToolingBootstrapRecoveredAfterRuntimeMutation() {
        var startupToolingBootstrapTask = Volatile.Read(ref _startupToolingBootstrapTask);
        if (startupToolingBootstrapTask is null || startupToolingBootstrapTask.IsCompletedSuccessfully) {
            return;
        }

        Volatile.Write(ref _startupToolingBootstrapTask, Task.CompletedTask);
    }

}
