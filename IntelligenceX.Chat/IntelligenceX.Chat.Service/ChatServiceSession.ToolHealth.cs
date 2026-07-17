using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service.Persistence;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int StartupToolHealthBackoffMaxExponent = 6;
    private const int MaximumStartupToolHealthCacheBytes = 1024 * 1024;
    private const int MaximumStartupToolHealthObservationCount = 4096;
    private const string StartupToolHealthCacheFileName = "startup-tool-health-cache-v1.json";
    private const string StartupToolHealthCacheStoreName = "Startup tool health cache";
    private static readonly JsonSerializerOptions StartupToolHealthCacheJson = new() {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
    internal readonly record struct ToolHealthProbeCatalogEntry(
        string ToolName,
        string PackId,
        string? PackName,
        ToolPackSourceKind SourceKind);

    private async Task HandleToolHealthAsync(StreamWriter writer, CheckToolHealthRequest request, CancellationToken cancellationToken) {
        var timeoutSeconds = request.ToolTimeoutSeconds ?? _options.ToolTimeoutSeconds;
        if (timeoutSeconds < ChatRequestOptionLimits.MinTimeoutSeconds || timeoutSeconds > ChatRequestOptionLimits.MaxTimeoutSeconds) {
            await WriteAsync(writer, new ErrorMessage {
                Kind = ChatServiceMessageKind.Response,
                RequestId = request.RequestId,
                Error = $"toolTimeoutSeconds must be between {ChatRequestOptionLimits.MinTimeoutSeconds} and {ChatRequestOptionLimits.MaxTimeoutSeconds}.",
                Code = "invalid_argument"
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var requireExplicitPackInfoRole = _runtimePolicyDiagnostics.RequireExplicitRoutingMetadata;
        var probeCatalog = GetToolHealthProbeCatalog(request.SourceKinds, request.PackIds);

        var probes = new List<ToolHealthProbeDto>(probeCatalog.Length);
        var okCount = 0;
        var failedCount = 0;
        foreach (var entry in probeCatalog) {
            var probe = await ToolHealthDiagnostics.ProbeAsync(
                    _registry,
                    entry.ToolName,
                    timeoutSeconds,
                    cancellationToken,
                    requireExplicitPackInfoRole)
                .ConfigureAwait(false);

            if (probe.Ok) {
                okCount++;
            } else {
                failedCount++;
            }

            probes.Add(MapProbeDto(probe, entry.PackId, entry.PackName, entry.SourceKind));
        }

        await WriteAsync(writer, new ToolHealthMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = request.RequestId,
            Probes = probes.ToArray(),
            OkCount = okCount,
            FailedCount = failedCount
        }, cancellationToken).ConfigureAwait(false);
    }

    private static ToolHealthProbeDto MapProbeDto(ToolHealthDiagnostics.ProbeResult probe, string? packId, string? packName, ToolPackSourceKind sourceKind) {
        var normalizedPackId = (packId ?? string.Empty).Trim();
        var normalizedPackName = (packName ?? string.Empty).Trim();
        return new ToolHealthProbeDto {
            ToolName = probe.ToolName,
            PackId = normalizedPackId.Length == 0 ? null : normalizedPackId,
            PackName = normalizedPackName.Length == 0 ? null : normalizedPackName,
            SourceKind = sourceKind,
            Ok = probe.Ok,
            ErrorCode = probe.Ok ? null : probe.ErrorCode,
            Error = probe.Ok ? null : probe.Error,
            DurationMs = probe.DurationMs
        };
    }

    private async Task PrimeStartupToolHealthWarningsAsync(CancellationToken cancellationToken) {
        var requireExplicitPackInfoRole = _runtimePolicyDiagnostics.RequireExplicitRoutingMetadata;
        var probeCatalog = GetToolHealthProbeCatalog();
        if (probeCatalog.Length == 0) {
            return;
        }

        var cache = LoadStartupToolHealthCache();
        var mutations = new List<StartupToolHealthCacheMutation>();

        try {
            foreach (var entry in probeCatalog) {
                cancellationToken.ThrowIfCancellationRequested();

                var cacheKey = BuildStartupToolHealthCacheKey(entry.SourceKind, entry.PackId, entry.ToolName);
                var nowUtc = DateTime.UtcNow;
                if (cache.TryGetValue(cacheKey, out var cachedFailure)
                    && ShouldSkipStartupToolHealthProbe(nowUtc, cachedFailure.NextProbeUtc)) {
                    continue;
                }

                var timeoutSeconds = ResolveStartupToolHealthTimeoutSeconds(_options.ToolTimeoutSeconds, entry.SourceKind, entry.PackId);
                var probe = await ToolHealthDiagnostics.ProbeAsync(
                        _registry,
                        entry.ToolName,
                        timeoutSeconds,
                        cancellationToken,
                        requireExplicitPackInfoRole)
                    .ConfigureAwait(false);
                if (!probe.Ok && IsToolTimeoutProbe(probe)) {
                    var retryTimeoutSeconds = ResolveStartupToolHealthRetryTimeoutSeconds(timeoutSeconds, entry.SourceKind, entry.PackId);
                    if (retryTimeoutSeconds > timeoutSeconds) {
                        probe = await ToolHealthDiagnostics.ProbeAsync(
                                _registry,
                                entry.ToolName,
                                retryTimeoutSeconds,
                                cancellationToken,
                                requireExplicitPackInfoRole)
                            .ConfigureAwait(false);
                    }
                }

                if (probe.Ok) {
                    var observedUtc = DateTime.UtcNow;
                    cache.Remove(cacheKey);
                    mutations.Add(new StartupToolHealthCacheMutation(cacheKey, Entry: null, ObservedUtc: observedUtc));
                    continue;
                }

                var failedUtc = DateTime.UtcNow;
                var sourceLabel = ToSourceLabel(entry.SourceKind);
                var packLabel = entry.PackId.Length == 0 ? "unknown" : entry.PackId;
                var prefix = ShouldDowngradeStartupToolHealthFailure(entry.SourceKind, probe.ErrorCode)
                    ? "[tool health notice]"
                    : "[tool health]";
                var warning = $"{prefix}[{sourceLabel}][{packLabel}] {probe.ToolName} failed ({NormalizeHealthErrorCode(probe.ErrorCode)}): {NormalizeHealthError(probe.Error)}";
                RecordStartupWarning(warning);

                var errorCode = NormalizeHealthErrorCode(probe.ErrorCode);
                var nextFailureCount = ResolveNextFailureCount(cachedFailure, errorCode);
                var nextProbeUtc = ComputeNextStartupToolHealthProbeUtc(
                    failedUtc,
                    entry.SourceKind,
                    entry.PackId,
                    errorCode,
                    nextFailureCount);
                var cacheEntry = new StartupToolHealthCacheEntry(
                    ErrorCode: errorCode,
                    Error: NormalizeHealthError(probe.Error),
                    LastFailedUtc: failedUtc,
                    NextProbeUtc: nextProbeUtc,
                    ConsecutiveFailures: nextFailureCount);
                cache[cacheKey] = cacheEntry;
                mutations.Add(new StartupToolHealthCacheMutation(cacheKey, cacheEntry, failedUtc));
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Startup priming is best-effort and bounded by the startup budget.
        } finally {
            if (mutations.Count > 0) {
                try {
                    if (!ApplyStartupToolHealthCacheMutations(ResolveStartupToolHealthCachePath(), mutations)) {
                        RecordStartupWarning("[tool health] Startup tool-health cache updates could not be persisted.");
                    }
                } catch (Exception ex) {
                    RecordStartupWarning("[tool health] Startup tool-health cache updates could not be persisted.");
                    Trace.TraceWarning($"{StartupToolHealthCacheStoreName} update failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

    }

    internal ToolHealthProbeCatalogEntry[] GetToolHealthProbeCatalog(IReadOnlyList<ToolPackSourceKind>? sourceKinds = null, IReadOnlyList<string>? packIds = null) {
        var requireExplicitPackInfoRole = _runtimePolicyDiagnostics.RequireExplicitRoutingMetadata;
        var packInfoCatalog = ToolHealthDiagnostics.BuildPackInfoProbeCatalog(_registry, _packAvailability, requireExplicitPackInfoRole);
        if (packInfoCatalog.Length == 0) {
            return Array.Empty<ToolHealthProbeCatalogEntry>();
        }

        var sourceFilter = BuildSourceKindFilter(sourceKinds);
        var packIdFilter = BuildPackIdFilter(packIds);
        var entries = new List<ToolHealthProbeCatalogEntry>(packInfoCatalog.Length);
        foreach (var entry in packInfoCatalog) {
            if (!ShouldIncludeProbe(entry.PackId, entry.SourceKind, sourceFilter, packIdFilter)) {
                continue;
            }

            entries.Add(new ToolHealthProbeCatalogEntry(
                ToolName: entry.ToolName,
                PackId: entry.PackId,
                PackName: entry.PackName,
                SourceKind: entry.SourceKind));
        }

        return entries.ToArray();
    }

    private static HashSet<ToolPackSourceKind>? BuildSourceKindFilter(IReadOnlyList<ToolPackSourceKind>? sourceKinds) {
        if (sourceKinds is null || sourceKinds.Count == 0) {
            return null;
        }

        var set = new HashSet<ToolPackSourceKind>();
        foreach (var sourceKind in sourceKinds) {
            set.Add(sourceKind);
        }

        return set.Count == 0 ? null : set;
    }

    private HashSet<string>? BuildPackIdFilter(IReadOnlyList<string>? packIds) {
        if (packIds is null || packIds.Count == 0) {
            return null;
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packId in packIds) {
            var normalized = NormalizePackId(packId);
            if (normalized.Length == 0) {
                continue;
            }

            set.Add(normalized);
        }

        return set.Count == 0 ? null : set;
    }

    private static bool ShouldIncludeProbe(string packId, ToolPackSourceKind sourceKind, HashSet<ToolPackSourceKind>? sourceFilter,
        HashSet<string>? packIdFilter) {
        if (sourceFilter is not null && !sourceFilter.Contains(sourceKind)) {
            return false;
        }

        if (packIdFilter is not null) {
            if (packId.Length == 0) {
                return false;
            }

            return packIdFilter.Contains(packId);
        }

        return true;
    }

    internal static int ResolveStartupToolHealthTimeoutSeconds(int configuredTimeoutSeconds, ToolPackSourceKind sourceKind, string? packId = null) {
        if (configuredTimeoutSeconds <= 0) {
            return sourceKind == ToolPackSourceKind.ClosedSource ? 8 : 4;
        }

        return sourceKind == ToolPackSourceKind.ClosedSource
            ? Math.Clamp(configuredTimeoutSeconds, 4, 20)
            : Math.Clamp(configuredTimeoutSeconds, 2, 10);
    }

    internal static int ResolveStartupToolHealthRetryTimeoutSeconds(int initialTimeoutSeconds, ToolPackSourceKind sourceKind, string? packId = null) {
        if (initialTimeoutSeconds <= 0) {
            return sourceKind == ToolPackSourceKind.ClosedSource ? 12 : 6;
        }

        var doubled = initialTimeoutSeconds * 2;
        return sourceKind == ToolPackSourceKind.ClosedSource
            ? Math.Clamp(doubled, 10, 30)
            : Math.Clamp(doubled, 4, 12);
    }

    internal static bool ShouldDowngradeStartupToolHealthFailure(ToolPackSourceKind sourceKind, string? errorCode) {
        return sourceKind == ToolPackSourceKind.ClosedSource
               && string.Equals((errorCode ?? string.Empty).Trim(), "tool_timeout", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldSkipStartupToolHealthProbe(DateTime nowUtc, DateTime nextProbeUtc) {
        return nextProbeUtc > nowUtc;
    }

    internal static DateTime ComputeNextStartupToolHealthProbeUtc(
        DateTime nowUtc,
        ToolPackSourceKind sourceKind,
        string? packId,
        string? errorCode,
        int consecutiveFailures) {
        var floor = ResolveStartupToolHealthBackoffFloorMinutes(sourceKind, packId, errorCode);
        var ceiling = ResolveStartupToolHealthBackoffCeilingMinutes(sourceKind, packId, errorCode);
        var exponent = Math.Clamp(consecutiveFailures - 1, 0, StartupToolHealthBackoffMaxExponent);
        var multiplier = 1 << exponent;
        var delayMinutes = Math.Min(ceiling, floor * multiplier);
        if (delayMinutes <= 0) {
            delayMinutes = floor;
        }

        return nowUtc.AddMinutes(delayMinutes);
    }

    private static bool IsToolTimeoutProbe(ToolHealthDiagnostics.ProbeResult probe) {
        return string.Equals(probe.ErrorCode, "tool_timeout", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHealthErrorCode(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? "tool_failed" : normalized;
    }

    private static string NormalizeHealthError(string? value) {
        var normalized = ToolHealthDiagnostics.CompactOneLine(value);
        return normalized.Length == 0 ? "Probe failed." : normalized;
    }

    private static string CompactStartupToolHealthException(Exception ex) {
        var message = ToolHealthDiagnostics.CompactOneLine(ex.Message);
        return message.Length == 0 ? ex.GetType().Name : message;
    }

    private static string ToSourceLabel(ToolPackSourceKind sourceKind) {
        return sourceKind switch {
            ToolPackSourceKind.Builtin => "builtin",
            ToolPackSourceKind.ClosedSource => "closed_source",
            ToolPackSourceKind.OpenSource => "open_source",
            _ => "unknown"
        };
    }

    private static int ResolveStartupToolHealthBackoffFloorMinutes(ToolPackSourceKind sourceKind, string? packId, string? errorCode) {
        var normalizedErrorCode = NormalizeHealthErrorCode(errorCode);
        var isTimeout = string.Equals(normalizedErrorCode, "tool_timeout", StringComparison.OrdinalIgnoreCase);

        if (isTimeout) {
            return sourceKind == ToolPackSourceKind.ClosedSource ? 10 : 5;
        }

        return sourceKind == ToolPackSourceKind.ClosedSource ? 4 : 2;
    }

    private static int ResolveStartupToolHealthBackoffCeilingMinutes(ToolPackSourceKind sourceKind, string? packId, string? errorCode) {
        var normalizedErrorCode = NormalizeHealthErrorCode(errorCode);
        var isTimeout = string.Equals(normalizedErrorCode, "tool_timeout", StringComparison.OrdinalIgnoreCase);

        if (isTimeout) {
            return sourceKind == ToolPackSourceKind.ClosedSource ? 120 : 45;
        }

        return sourceKind == ToolPackSourceKind.ClosedSource ? 30 : 20;
    }

    private static string BuildStartupToolHealthCacheKey(ToolPackSourceKind sourceKind, string packId, string toolName) {
        return ToSourceLabel(sourceKind)
               + "|"
               + ToolPackBootstrap.NormalizePackId(packId)
               + "|"
               + (toolName ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static int ResolveNextFailureCount(StartupToolHealthCacheEntry? previous, string nextErrorCode) {
        if (previous is null) {
            return 1;
        }

        if (string.Equals(previous.ErrorCode, nextErrorCode, StringComparison.OrdinalIgnoreCase)) {
            return Math.Clamp(previous.ConsecutiveFailures + 1, 1, 64);
        }

        return 1;
    }

    private static Dictionary<string, StartupToolHealthCacheEntry> LoadStartupToolHealthCache() {
        return LoadStartupToolHealthCache(ResolveStartupToolHealthCachePath());
    }

    internal static Dictionary<string, StartupToolHealthCacheEntry> LoadStartupToolHealthCache(string path) {
        return LoadStartupToolHealthCacheState(path).Entries;
    }

    private static StartupToolHealthCacheState LoadStartupToolHealthCacheState(string path) {
        var result = ChatServiceJsonFileStore.Read<StartupToolHealthCacheState>(
            path,
            MaximumStartupToolHealthCacheBytes,
            DeserializeStartupToolHealthCacheState,
            static _ => true,
            normalize: null,
            StartupToolHealthCacheStoreName);
        return result.State == ChatServiceJsonFileReadState.Loaded && result.Value is not null
            ? result.Value
            : StartupToolHealthCacheState.Empty();
    }

    internal static Dictionary<string, StartupToolHealthCacheEntry> DeserializeStartupToolHealthCache(string json) {
        return DeserializeStartupToolHealthCacheState(json).Entries;
    }

    private static StartupToolHealthCacheState DeserializeStartupToolHealthCacheState(string json) {
        var payload = JsonSerializer.Deserialize<StartupToolHealthCachePayload>(json, StartupToolHealthCacheJson);
        var entries = payload?.Entries;

        var state = StartupToolHealthCacheState.Empty();
        if (entries is not null) {
            for (var i = 0; i < entries.Count; i++) {
                var entry = entries[i];
                var key = (entry?.Key ?? string.Empty).Trim();
                if (key.Length == 0) {
                    continue;
                }

                var cacheEntry = new StartupToolHealthCacheEntry(
                    ErrorCode: NormalizeHealthErrorCode(entry!.ErrorCode),
                    Error: NormalizeHealthError(entry.Error),
                    LastFailedUtc: entry.LastFailedUtc,
                    NextProbeUtc: entry.NextProbeUtc,
                    ConsecutiveFailures: Math.Clamp(entry.ConsecutiveFailures, 1, 64));
                state.Entries[key] = cacheEntry;
                RememberLatestStartupToolHealthObservation(
                    state.LatestObservations,
                    key,
                    new StartupToolHealthObservation(
                        NormalizeStartupToolHealthObservationUtc(cacheEntry.LastFailedUtc),
                        Successful: false));
            }
        }

        var observations = payload?.LatestObservations;
        if (observations is not null) {
            for (var i = 0; i < observations.Count; i++) {
                var observation = observations[i];
                var key = (observation?.Key ?? string.Empty).Trim();
                if (key.Length == 0) {
                    continue;
                }

                RememberLatestStartupToolHealthObservation(
                    state.LatestObservations,
                    key,
                    new StartupToolHealthObservation(
                        NormalizeStartupToolHealthObservationUtc(observation!.ObservedUtc),
                        observation.Successful));
            }
        }

        return state;
    }

    internal static bool ApplyStartupToolHealthCacheMutations(
        string path,
        IReadOnlyList<StartupToolHealthCacheMutation> mutations) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(mutations);
        if (mutations.Count == 0) {
            return true;
        }

        var normalizedPath = Path.GetFullPath(path);
        var lockAcquired = ChatServiceJsonFileStore.TryWithExclusiveAccess(
            normalizedPath,
            static state => ApplyStartupToolHealthCacheMutationsNoLock(state.Path, state.Mutations),
            (Path: normalizedPath, Mutations: mutations),
            out var applied,
            StartupToolHealthCacheStoreName);
        return lockAcquired && applied;
    }

    private static bool ApplyStartupToolHealthCacheMutationsNoLock(
        string path,
        IReadOnlyList<StartupToolHealthCacheMutation> mutations) {
        var state = LoadStartupToolHealthCacheState(path);
        var changed = false;
        foreach (var mutation in mutations) {
            var key = (mutation.Key ?? string.Empty).Trim();
            if (key.Length == 0) {
                continue;
            }

            var nextObservation = new StartupToolHealthObservation(
                NormalizeStartupToolHealthObservationUtc(mutation.ObservedUtc),
                Successful: mutation.Entry is null);
            if (state.LatestObservations.TryGetValue(key, out var latestObservation)
                && (nextObservation.ObservedUtc < latestObservation.ObservedUtc
                    || (nextObservation.ObservedUtc == latestObservation.ObservedUtc
                        && latestObservation.Successful
                        && !nextObservation.Successful))) {
                continue;
            }

            if (!state.LatestObservations.TryGetValue(key, out latestObservation)
                || latestObservation != nextObservation) {
                state.LatestObservations[key] = nextObservation;
                changed = true;
            }

            if (mutation.Entry is null) {
                changed |= state.Entries.Remove(key);
                continue;
            }

            if (!state.Entries.TryGetValue(key, out var existing) || existing != mutation.Entry) {
                state.Entries[key] = mutation.Entry;
                changed = true;
            }
        }

        return !changed || SaveStartupToolHealthCache(path, state);
    }

    private static DateTime NormalizeStartupToolHealthObservationUtc(DateTime value) {
        return value.Kind switch {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static bool SaveStartupToolHealthCache(
        string path,
        StartupToolHealthCacheState state) {
        var payload = new StartupToolHealthCachePayload {
            Entries = state.Entries
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => new StartupToolHealthCachePayloadEntry {
                    Key = pair.Key,
                    ErrorCode = pair.Value.ErrorCode,
                    Error = pair.Value.Error,
                    LastFailedUtc = pair.Value.LastFailedUtc,
                    NextProbeUtc = pair.Value.NextProbeUtc,
                    ConsecutiveFailures = pair.Value.ConsecutiveFailures
                })
                .ToList(),
            LatestObservations = state.LatestObservations
                .OrderByDescending(static pair => pair.Value.ObservedUtc)
                .Take(MaximumStartupToolHealthObservationCount)
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => new StartupToolHealthCachePayloadObservation {
                    Key = pair.Key,
                    ObservedUtc = pair.Value.ObservedUtc,
                    Successful = pair.Value.Successful
                })
                .ToList()
        };

        return ChatServiceJsonFileStore.Write(
            path,
            payload,
            static value => JsonSerializer.Serialize(value, StartupToolHealthCacheJson),
            StartupToolHealthCacheStoreName);
    }

    private static void RememberLatestStartupToolHealthObservation(
        Dictionary<string, StartupToolHealthObservation> observations,
        string key,
        StartupToolHealthObservation candidate) {
        if (!observations.TryGetValue(key, out var current)
            || candidate.ObservedUtc > current.ObservedUtc
            || (candidate.ObservedUtc == current.ObservedUtc && candidate.Successful && !current.Successful)) {
            observations[key] = candidate;
        }
    }

    private static string ResolveStartupToolHealthCachePath() {
        return ChatServiceJsonFileStore.ResolveDefaultPath(StartupToolHealthCacheFileName);
    }

    internal sealed record StartupToolHealthCacheEntry(
        string ErrorCode,
        string Error,
        DateTime LastFailedUtc,
        DateTime NextProbeUtc,
        int ConsecutiveFailures);

    internal sealed record StartupToolHealthCacheMutation(
        string Key,
        StartupToolHealthCacheEntry? Entry,
        DateTime ObservedUtc);

    private sealed class StartupToolHealthCachePayload {
        public List<StartupToolHealthCachePayloadEntry> Entries { get; set; } = new();
        public List<StartupToolHealthCachePayloadObservation> LatestObservations { get; set; } = new();
    }

    private sealed class StartupToolHealthCachePayloadEntry {
        public string Key { get; set; } = string.Empty;
        public string? ErrorCode { get; set; }
        public string? Error { get; set; }
        public DateTime LastFailedUtc { get; set; }
        public DateTime NextProbeUtc { get; set; }
        public int ConsecutiveFailures { get; set; } = 1;
    }

    private sealed class StartupToolHealthCachePayloadObservation {
        public string Key { get; set; } = string.Empty;
        public DateTime ObservedUtc { get; set; }
        public bool Successful { get; set; }
    }

    private sealed record StartupToolHealthCacheState(
        Dictionary<string, StartupToolHealthCacheEntry> Entries,
        Dictionary<string, StartupToolHealthObservation> LatestObservations) {
        internal static StartupToolHealthCacheState Empty() => new(
            new Dictionary<string, StartupToolHealthCacheEntry>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, StartupToolHealthObservation>(StringComparer.OrdinalIgnoreCase));
    }

    private readonly record struct StartupToolHealthObservation(DateTime ObservedUtc, bool Successful);

    private void RecordStartupWarning(string? warning) {
        var normalized = (warning ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return;
        }

        if (_startupWarnings.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))) {
            return;
        }

        var warnings = new List<string>(_startupWarnings) {
            normalized
        };
        _startupWarnings = NormalizeDistinctStrings(warnings, maxItems: 96);
        Trace.TraceWarning(normalized);
    }

    internal static string BuildStartupToolHealthPrimingFailureWarning(Exception ex) {
        return $"[tool health] Startup probe priming failed: {CompactStartupToolHealthException(ex)}";
    }

    internal static async Task RunStartupToolHealthPrimingAsync(
        Func<CancellationToken, Task> primeAsync,
        Action<string> recordWarning,
        TimeSpan primeBudget,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(primeAsync);
        ArgumentNullException.ThrowIfNull(recordWarning);

        try {
            using var startupToolHealthCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (primeBudget > TimeSpan.Zero) {
                startupToolHealthCts.CancelAfter(primeBudget);
            }

            await primeAsync(startupToolHealthCts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Session shutdown canceled startup priming.
        } catch (Exception ex) {
            recordWarning(BuildStartupToolHealthPrimingFailureWarning(ex));
        }
    }

    private Task RunStartupToolHealthPrimingAsync(CancellationToken cancellationToken) {
        return RunStartupToolHealthPrimingAsync(
            PrimeStartupToolHealthWarningsAsync,
            RecordStartupWarning,
            StartupToolHealthPrimeBudget,
            cancellationToken);
    }

    private async Task RunStartupToolHealthPrimingAfterToolingBootstrapAsync(
        Task startupToolingBootstrapTask,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(startupToolingBootstrapTask);

        try {
            await startupToolingBootstrapTask.ConfigureAwait(false);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            return;
        } catch (Exception ex) {
            RecordStartupWarning(
                "[tool health] Startup probe priming skipped because tool bootstrap failed: "
                + CompactStartupToolHealthException(ex));
            return;
        }

        await RunStartupToolHealthPrimingAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Task AwaitStartupToolHealthPrimingForHelloAsync(Task startupToolHealthPrimeTask, CancellationToken cancellationToken) {
        return AwaitStartupToolHealthPrimingForHelloAsync(startupToolHealthPrimeTask, StartupToolHealthHelloWaitBudget, cancellationToken);
    }

    internal static async Task AwaitStartupToolHealthPrimingForHelloAsync(
        Task startupToolHealthPrimeTask,
        TimeSpan waitBudget,
        CancellationToken cancellationToken) {
        if (startupToolHealthPrimeTask.IsCompleted) {
            await startupToolHealthPrimeTask.ConfigureAwait(false);
            return;
        }

        if (waitBudget <= TimeSpan.Zero) {
            return;
        }

        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(waitBudget, waitCts.Token);

        Task completedTask;
        try {
            completedTask = await Task.WhenAny(startupToolHealthPrimeTask, delayTask).ConfigureAwait(false);
        } finally {
            waitCts.Cancel();
            try {
                await delayTask.ConfigureAwait(false);
            } catch (OperationCanceledException) {
                // Expected when startup priming completes before the wait budget.
            }
        }

        if (completedTask == startupToolHealthPrimeTask) {
            await startupToolHealthPrimeTask.ConfigureAwait(false);
        }
    }
}
