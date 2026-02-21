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
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int StartupToolHealthBackoffMaxExponent = 6;
    private const string StartupToolHealthCacheFileName = "startup-tool-health-cache-v1.json";
    private static readonly JsonSerializerOptions StartupToolHealthCacheJson = new() {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

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

        var packInfoDefinitions = _registry.GetDefinitions()
            .Where(static def => def.Name.EndsWith("_pack_info", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static def => def.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceFilter = BuildSourceKindFilter(request.SourceKinds);
        var packIdFilter = BuildPackIdFilter(request.PackIds);

        var probes = new List<ToolHealthProbeDto>(packInfoDefinitions.Length);
        var okCount = 0;
        var failedCount = 0;
        foreach (var definition in packInfoDefinitions) {
            var metadata = ResolvePackMetadata(definition);
            if (!ShouldIncludeProbe(metadata.PackId, metadata.SourceKind, sourceFilter, packIdFilter)) {
                continue;
            }

            var probe = await ToolHealthDiagnostics.ProbeAsync(_registry, definition.Name, timeoutSeconds, cancellationToken)
                .ConfigureAwait(false);

            if (probe.Ok) {
                okCount++;
            } else {
                failedCount++;
            }

            probes.Add(MapProbeDto(probe, metadata.PackId, metadata.PackName, metadata.SourceKind));
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
        var packInfoDefinitions = _registry.GetDefinitions()
            .Where(static def => def.Name.EndsWith("_pack_info", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static def => def.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (packInfoDefinitions.Length == 0) {
            return;
        }

        var cache = LoadStartupToolHealthCache();
        var cacheUpdated = false;

        try {
            foreach (var definition in packInfoDefinitions) {
                cancellationToken.ThrowIfCancellationRequested();

                var metadata = ResolvePackMetadata(definition);
                var cacheKey = BuildStartupToolHealthCacheKey(metadata.SourceKind, metadata.PackId, definition.Name);
                var nowUtc = DateTime.UtcNow;
                if (cache.TryGetValue(cacheKey, out var cachedFailure)
                    && ShouldSkipStartupToolHealthProbe(nowUtc, cachedFailure.NextProbeUtc)) {
                    continue;
                }

                var timeoutSeconds = ResolveStartupToolHealthTimeoutSeconds(_options.ToolTimeoutSeconds, metadata.SourceKind, metadata.PackId);
                var probe = await ToolHealthDiagnostics.ProbeAsync(_registry, definition.Name, timeoutSeconds, cancellationToken).ConfigureAwait(false);
                if (!probe.Ok && IsToolTimeoutProbe(probe)) {
                    var retryTimeoutSeconds = ResolveStartupToolHealthRetryTimeoutSeconds(timeoutSeconds, metadata.SourceKind, metadata.PackId);
                    if (retryTimeoutSeconds > timeoutSeconds) {
                        probe = await ToolHealthDiagnostics.ProbeAsync(_registry, definition.Name, retryTimeoutSeconds, cancellationToken).ConfigureAwait(false);
                    }
                }

                if (probe.Ok) {
                    if (cache.Remove(cacheKey)) {
                        cacheUpdated = true;
                    }
                    continue;
                }

                var sourceLabel = ToSourceLabel(metadata.SourceKind);
                var packLabel = metadata.PackId.Length == 0 ? "unknown" : metadata.PackId;
                var prefix = ShouldDowngradeStartupToolHealthFailure(metadata.SourceKind, probe.ErrorCode)
                    ? "[tool health notice]"
                    : "[tool health]";
                var warning = $"{prefix}[{sourceLabel}][{packLabel}] {probe.ToolName} failed ({NormalizeHealthErrorCode(probe.ErrorCode)}): {NormalizeHealthError(probe.Error)}";
                RecordStartupWarning(warning);

                var errorCode = NormalizeHealthErrorCode(probe.ErrorCode);
                var nextFailureCount = ResolveNextFailureCount(cachedFailure, errorCode);
                var nextProbeUtc = ComputeNextStartupToolHealthProbeUtc(
                    nowUtc,
                    metadata.SourceKind,
                    metadata.PackId,
                    errorCode,
                    nextFailureCount);
                cache[cacheKey] = new StartupToolHealthCacheEntry(
                    ErrorCode: errorCode,
                    Error: NormalizeHealthError(probe.Error),
                    LastFailedUtc: nowUtc,
                    NextProbeUtc: nextProbeUtc,
                    ConsecutiveFailures: nextFailureCount);
                cacheUpdated = true;
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Startup priming is best-effort and bounded by the startup budget.
        } finally {
            if (cacheUpdated) {
                SaveStartupToolHealthCache(cache);
            }
        }

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

    private (string PackId, string? PackName, ToolPackSourceKind SourceKind) ResolvePackMetadata(ToolDefinition definition) {
        var packId = string.Empty;
        if (_toolPackIdsByToolName.TryGetValue(definition.Name, out var assignedPackId)) {
            packId = NormalizePackId(assignedPackId);
        }

        _packDisplayNamesById.TryGetValue(packId, out var packName);
        var sourceKind = ToolPackSourceKind.OpenSource;
        if (packId.Length > 0 && _packSourceKindsById.TryGetValue(packId, out var resolved)) {
            sourceKind = resolved;
        }
        return (packId, packName, sourceKind);
    }

    internal static int ResolveStartupToolHealthTimeoutSeconds(int configuredTimeoutSeconds, ToolPackSourceKind sourceKind, string? packId = null) {
        var normalizedPackId = ToolPackBootstrap.NormalizePackId(packId);
        var isTestimoX = string.Equals(normalizedPackId, "testimox", StringComparison.OrdinalIgnoreCase);
        if (configuredTimeoutSeconds <= 0) {
            var timeout = sourceKind == ToolPackSourceKind.ClosedSource ? 8 : 4;
            return isTestimoX
                ? Math.Clamp(Math.Max(timeout, 12), 12, 30)
                : timeout;
        }

        if (isTestimoX) {
            return Math.Clamp(configuredTimeoutSeconds, 12, 30);
        }

        return sourceKind == ToolPackSourceKind.ClosedSource
            ? Math.Clamp(configuredTimeoutSeconds, 4, 20)
            : Math.Clamp(configuredTimeoutSeconds, 2, 10);
    }

    internal static int ResolveStartupToolHealthRetryTimeoutSeconds(int initialTimeoutSeconds, ToolPackSourceKind sourceKind, string? packId = null) {
        var normalizedPackId = ToolPackBootstrap.NormalizePackId(packId);
        if (initialTimeoutSeconds <= 0) {
            var timeout = sourceKind == ToolPackSourceKind.ClosedSource ? 12 : 6;
            return string.Equals(normalizedPackId, "testimox", StringComparison.OrdinalIgnoreCase)
                ? Math.Clamp(Math.Max(timeout, 20), 20, 45)
                : timeout;
        }

        var doubled = initialTimeoutSeconds * 2;
        var resolved = sourceKind == ToolPackSourceKind.ClosedSource
            ? Math.Clamp(doubled, 10, 30)
            : Math.Clamp(doubled, 4, 12);
        return string.Equals(normalizedPackId, "testimox", StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp(Math.Max(resolved, 20), 20, 45)
            : resolved;
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
        var normalizedPackId = ToolPackBootstrap.NormalizePackId(packId);
        var normalizedErrorCode = NormalizeHealthErrorCode(errorCode);
        var isTimeout = string.Equals(normalizedErrorCode, "tool_timeout", StringComparison.OrdinalIgnoreCase);
        var isTestimoX = string.Equals(normalizedPackId, "testimox", StringComparison.OrdinalIgnoreCase);

        if (isTimeout) {
            if (isTestimoX) {
                return 20;
            }

            return sourceKind == ToolPackSourceKind.ClosedSource ? 10 : 5;
        }

        return sourceKind == ToolPackSourceKind.ClosedSource ? 4 : 2;
    }

    private static int ResolveStartupToolHealthBackoffCeilingMinutes(ToolPackSourceKind sourceKind, string? packId, string? errorCode) {
        var normalizedPackId = ToolPackBootstrap.NormalizePackId(packId);
        var normalizedErrorCode = NormalizeHealthErrorCode(errorCode);
        var isTimeout = string.Equals(normalizedErrorCode, "tool_timeout", StringComparison.OrdinalIgnoreCase);
        var isTestimoX = string.Equals(normalizedPackId, "testimox", StringComparison.OrdinalIgnoreCase);

        if (isTimeout) {
            if (isTestimoX) {
                return 180;
            }

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
        var path = ResolveStartupToolHealthCachePath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            return new Dictionary<string, StartupToolHealthCacheEntry>(StringComparer.OrdinalIgnoreCase);
        }

        try {
            var json = File.ReadAllText(path);
            var payload = JsonSerializer.Deserialize<StartupToolHealthCachePayload>(json, StartupToolHealthCacheJson);
            if (payload?.Entries is null || payload.Entries.Count == 0) {
                return new Dictionary<string, StartupToolHealthCacheEntry>(StringComparer.OrdinalIgnoreCase);
            }

            var map = new Dictionary<string, StartupToolHealthCacheEntry>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < payload.Entries.Count; i++) {
                var entry = payload.Entries[i];
                if (string.IsNullOrWhiteSpace(entry.Key)) {
                    continue;
                }

                map[entry.Key] = new StartupToolHealthCacheEntry(
                    ErrorCode: NormalizeHealthErrorCode(entry.ErrorCode),
                    Error: NormalizeHealthError(entry.Error),
                    LastFailedUtc: entry.LastFailedUtc,
                    NextProbeUtc: entry.NextProbeUtc,
                    ConsecutiveFailures: Math.Clamp(entry.ConsecutiveFailures, 1, 64));
            }

            return map;
        } catch {
            return new Dictionary<string, StartupToolHealthCacheEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveStartupToolHealthCache(Dictionary<string, StartupToolHealthCacheEntry> cache) {
        var path = ResolveStartupToolHealthCachePath();
        if (string.IsNullOrWhiteSpace(path)) {
            return;
        }

        try {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var payload = new StartupToolHealthCachePayload {
                Entries = cache
                    .Select(static pair => new StartupToolHealthCachePayloadEntry {
                        Key = pair.Key,
                        ErrorCode = pair.Value.ErrorCode,
                        Error = pair.Value.Error,
                        LastFailedUtc = pair.Value.LastFailedUtc,
                        NextProbeUtc = pair.Value.NextProbeUtc,
                        ConsecutiveFailures = pair.Value.ConsecutiveFailures
                    })
                    .ToList()
            };

            var json = JsonSerializer.Serialize(payload, StartupToolHealthCacheJson);
            File.WriteAllText(path, json);
        } catch {
            // Ignore cache write failures.
        }
    }

    private static string ResolveStartupToolHealthCachePath() {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData)) {
            return Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat", StartupToolHealthCacheFileName);
        }

        return Path.Combine(localAppData, "IntelligenceX.Chat", StartupToolHealthCacheFileName);
    }

    private sealed record StartupToolHealthCacheEntry(
        string ErrorCode,
        string Error,
        DateTime LastFailedUtc,
        DateTime NextProbeUtc,
        int ConsecutiveFailures);

    private sealed class StartupToolHealthCachePayload {
        public List<StartupToolHealthCachePayloadEntry> Entries { get; set; } = new();
    }

    private sealed class StartupToolHealthCachePayloadEntry {
        public string Key { get; set; } = string.Empty;
        public string? ErrorCode { get; set; }
        public string? Error { get; set; }
        public DateTime LastFailedUtc { get; set; }
        public DateTime NextProbeUtc { get; set; }
        public int ConsecutiveFailures { get; set; } = 1;
    }

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
