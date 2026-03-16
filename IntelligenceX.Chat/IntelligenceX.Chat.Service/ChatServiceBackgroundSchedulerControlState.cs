using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Tooling;

namespace IntelligenceX.Chat.Service;

internal sealed class ChatServiceBackgroundSchedulerControlState {
    private const int StoreVersion = 4;
    private const int MaxPauseReasonLength = 120;
    private const int MaxMaintenanceWindowSpecLength = 160;
    private const int MaxMaintenanceWindowThreadScopeLength = 120;
    private static readonly object StoreLock = new();
    private static readonly JsonSerializerOptions StoreJsonOptions = new() {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

    private readonly object _lock = new();
    private readonly ServiceOptions _options;
    private readonly string[] _defaultBlockedPackIds;
    private readonly string[] _defaultBlockedThreadIds;
    private readonly MaintenanceWindow[] _defaultMaintenanceWindows;
    private string[] _blockedPackIds;
    private bool _blockedPackIdsCustomized;
    private TemporarySuppression[] _temporaryBlockedPackSuppressions = Array.Empty<TemporarySuppression>();
    private string[] _blockedThreadIds;
    private bool _blockedThreadIdsCustomized;
    private TemporarySuppression[] _temporaryBlockedThreadSuppressions = Array.Empty<TemporarySuppression>();
    private MaintenanceWindow[] _maintenanceWindows;
    private bool _maintenanceWindowsCustomized;
    private bool _manualPauseActive;
    private long _pausedUntilUtcTicks;
    private string _pauseReason = string.Empty;

    private sealed class StoreDto {
        public int Version { get; set; } = StoreVersion;
        public bool ManualPauseActive { get; set; }
        public long PausedUntilUtcTicks { get; set; }
        public string PauseReason { get; set; } = string.Empty;
        public bool BlockedPackIdsCustomized { get; set; }
        public string[] BlockedPackIds { get; set; } = Array.Empty<string>();
        public TemporarySuppressionStoreDto[] TemporaryBlockedPacks { get; set; } = Array.Empty<TemporarySuppressionStoreDto>();
        public bool BlockedThreadIdsCustomized { get; set; }
        public string[] BlockedThreadIds { get; set; } = Array.Empty<string>();
        public TemporarySuppressionStoreDto[] TemporaryBlockedThreads { get; set; } = Array.Empty<TemporarySuppressionStoreDto>();
        public bool MaintenanceWindowsCustomized { get; set; }
        public string[] MaintenanceWindowSpecs { get; set; } = Array.Empty<string>();
    }

    private readonly record struct MaintenanceWindow(
        byte DayMask,
        int StartMinuteOfDay,
        int DurationMinutes,
        string PackId,
        string ThreadId,
        string NormalizedSpec);

    private sealed class TemporarySuppressionStoreDto {
        public string Id { get; set; } = string.Empty;
        public long ExpiresUtcTicks { get; set; }
    }

    private readonly record struct TemporarySuppression(
        string Id,
        long ExpiresUtcTicks);

    internal ChatServiceBackgroundSchedulerControlState(ServiceOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _defaultBlockedPackIds = NormalizeBlockedPackIds(_options.BackgroundSchedulerBlockedPackIds);
        _blockedPackIds = _defaultBlockedPackIds;
        _defaultBlockedThreadIds = NormalizeBlockedThreadIds(_options.BackgroundSchedulerBlockedThreadIds);
        _blockedThreadIds = _defaultBlockedThreadIds;
        _defaultMaintenanceWindows = BuildMaintenanceWindows(_options.BackgroundSchedulerMaintenanceWindows);
        _maintenanceWindows = _defaultMaintenanceWindows;

        var persisted = ReadStoreStateNoThrow();
        if (persisted.ManualPauseActive) {
            _manualPauseActive = true;
            _pausedUntilUtcTicks = persisted.PausedUntilUtcTicks;
            _pauseReason = persisted.PauseReason;
        }
        if (persisted.BlockedPackIdsCustomized) {
            _blockedPackIdsCustomized = true;
            _blockedPackIds = NormalizeBlockedPackIds(persisted.BlockedPackIds);
        }
        _temporaryBlockedPackSuppressions = NormalizeTemporarySuppressions(
            persisted.TemporaryBlockedPacks,
            maxIdLength: ChatRequestOptionLimits.MaxToolSelectorLength,
            normalizeId: ToolPackBootstrap.NormalizePackId,
            StringComparer.OrdinalIgnoreCase,
            DateTime.UtcNow.Ticks);
        if (persisted.BlockedThreadIdsCustomized) {
            _blockedThreadIdsCustomized = true;
            _blockedThreadIds = NormalizeBlockedThreadIds(persisted.BlockedThreadIds);
        }
        _temporaryBlockedThreadSuppressions = NormalizeTemporarySuppressions(
            persisted.TemporaryBlockedThreads,
            maxIdLength: ChatRequestOptionLimits.MaxToolSelectorLength,
            normalizeId: NormalizeMaintenanceWindowThreadId,
            StringComparer.Ordinal,
            DateTime.UtcNow.Ticks);
        if (persisted.MaintenanceWindowsCustomized) {
            _maintenanceWindowsCustomized = true;
            _maintenanceWindows = BuildMaintenanceWindows(persisted.MaintenanceWindowSpecs);
        }

        var snapshot = GetSnapshot(DateTime.UtcNow.Ticks);
        if (!snapshot.ManualPauseActive && _options.BackgroundSchedulerStartPaused) {
            int? pauseSeconds = _options.BackgroundSchedulerStartupPauseSeconds > 0
                ? _options.BackgroundSchedulerStartupPauseSeconds
                : null;
            SetManualPause(
                paused: true,
                pauseSeconds,
                "startup");
        }
    }

    internal BackgroundSchedulerPauseStateSnapshot GetSnapshot(long nowTicks) {
        lock (_lock) {
            var expired = NormalizeExpiredManualPauseNoLock(nowTicks);
            var snapshot = BuildSnapshotNoLock(nowTicks);
            if (expired) {
                PersistStateNoThrow();
            }

            return snapshot;
        }
    }

    internal BackgroundSchedulerPauseStateSnapshot SetManualPause(bool paused, int? pauseSeconds, string pauseReason) {
        var nowTicks = DateTime.UtcNow.Ticks;
        BackgroundSchedulerPauseStateSnapshot snapshot;
        lock (_lock) {
            if (!paused) {
                _manualPauseActive = false;
                _pausedUntilUtcTicks = 0;
                _pauseReason = string.Empty;
            } else {
                _manualPauseActive = true;
                _pausedUntilUtcTicks = pauseSeconds is > 0
                    ? nowTicks + TimeSpan.FromSeconds(pauseSeconds.Value).Ticks
                    : 0;
                _pauseReason = NormalizeManualPauseReason(pauseReason, pauseSeconds);
            }

            snapshot = new BackgroundSchedulerPauseStateSnapshot(
                ManualPauseActive: _manualPauseActive,
                ScheduledPauseActive: false,
                PausedUntilUtcTicks: _pausedUntilUtcTicks,
                PauseReason: _pauseReason);
        }

        PersistStateNoThrow();
        return snapshot;
    }

    internal static string NormalizeManualPauseReason(string? reason, int? pauseSeconds) {
        var normalizedReason = NormalizeActivityText(reason, MaxPauseReasonLength);
        var prefix = pauseSeconds is > 0
            ? $"manual_pause:{pauseSeconds.Value}s"
            : "manual_pause";
        return normalizedReason.Length == 0 ? prefix : prefix + ":" + normalizedReason;
    }

    internal string[] GetMaintenanceWindowSpecs() {
        lock (_lock) {
            return _maintenanceWindows
                .Select(static window => window.NormalizedSpec)
                .ToArray();
        }
    }

    internal string[] GetBlockedPackIds(long nowTicks) {
        lock (_lock) {
            if (NormalizeExpiredTemporarySuppressionsNoLock(nowTicks)) {
                PersistStateNoThrow();
            }

            return BuildCombinedSuppressionIdsNoLock(_blockedPackIds, _temporaryBlockedPackSuppressions, StringComparer.OrdinalIgnoreCase);
        }
    }

    internal SessionCapabilityBackgroundSchedulerSuppressionDto[] GetBlockedPackSuppressions(long nowTicks) {
        lock (_lock) {
            if (NormalizeExpiredTemporarySuppressionsNoLock(nowTicks)) {
                PersistStateNoThrow();
            }

            return BuildSuppressionDtosNoLock(
                _defaultBlockedPackIds,
                _blockedPackIds,
                _temporaryBlockedPackSuppressions,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    internal string[] GetBlockedThreadIds(long nowTicks) {
        lock (_lock) {
            if (NormalizeExpiredTemporarySuppressionsNoLock(nowTicks)) {
                PersistStateNoThrow();
            }

            return BuildCombinedSuppressionIdsNoLock(_blockedThreadIds, _temporaryBlockedThreadSuppressions, StringComparer.Ordinal);
        }
    }

    internal SessionCapabilityBackgroundSchedulerSuppressionDto[] GetBlockedThreadSuppressions(long nowTicks) {
        lock (_lock) {
            if (NormalizeExpiredTemporarySuppressionsNoLock(nowTicks)) {
                PersistStateNoThrow();
            }

            return BuildSuppressionDtosNoLock(
                _defaultBlockedThreadIds,
                _blockedThreadIds,
                _temporaryBlockedThreadSuppressions,
                StringComparer.Ordinal);
        }
    }

    internal SessionCapabilityBackgroundSchedulerMaintenanceWindowDto[] GetMaintenanceWindows() {
        lock (_lock) {
            return _maintenanceWindows
                .Select(ToMaintenanceWindowDto)
                .ToArray();
        }
    }

    internal string[] GetActiveMaintenanceWindowSpecs(long nowTicks) {
        lock (_lock) {
            return GetActiveMaintenanceWindowsNoLock(nowTicks)
                .Select(static window => window.NormalizedSpec)
                .ToArray();
        }
    }

    internal SessionCapabilityBackgroundSchedulerMaintenanceWindowDto[] GetActiveMaintenanceWindows(long nowTicks) {
        lock (_lock) {
            return GetActiveMaintenanceWindowsNoLock(nowTicks)
                .Select(ToMaintenanceWindowDto)
                .ToArray();
        }
    }

    internal bool TryGetScopedMaintenanceWindowPause(
        long nowTicks,
        string? threadId,
        string? packId,
        out BackgroundSchedulerPauseStateSnapshot snapshot) {
        lock (_lock) {
            return TryGetScopedMaintenanceWindowPauseNoLock(
                nowTicks,
                NormalizeMaintenanceWindowThreadId(threadId),
                ToolPackBootstrap.NormalizePackId(packId),
                out snapshot);
        }
    }

    internal void UpdateMaintenanceWindows(string operation, IReadOnlyList<string>? rawSpecs) {
        var normalizedOperation = NormalizeMaintenanceWindowOperation(operation);
        var normalizedSpecs = NormalizeMaintenanceWindowSpecs(rawSpecs);
        lock (_lock) {
            switch (normalizedOperation) {
                case "add":
                    _maintenanceWindowsCustomized = true;
                    _maintenanceWindows = MergeMaintenanceWindows(_maintenanceWindows, normalizedSpecs);
                    break;
                case "remove":
                    _maintenanceWindowsCustomized = true;
                    _maintenanceWindows = RemoveMaintenanceWindows(_maintenanceWindows, normalizedSpecs);
                    break;
                case "replace":
                    _maintenanceWindowsCustomized = true;
                    _maintenanceWindows = BuildMaintenanceWindows(normalizedSpecs);
                    break;
                case "clear":
                    _maintenanceWindowsCustomized = true;
                    _maintenanceWindows = Array.Empty<MaintenanceWindow>();
                    break;
                case "reset":
                    _maintenanceWindowsCustomized = false;
                    _maintenanceWindows = _defaultMaintenanceWindows;
                    break;
            }
        }

        PersistStateNoThrow();
    }

    internal void UpdateBlockedPacks(string operation, IReadOnlyList<string>? rawPackIds, int? durationSeconds = null) {
        var normalizedOperation = NormalizeMaintenanceWindowOperation(operation);
        var normalizedPackIds = NormalizeBlockedPackIds(rawPackIds);
        var nowTicks = DateTime.UtcNow.Ticks;
        lock (_lock) {
            NormalizeExpiredTemporarySuppressionsNoLock(nowTicks);
            switch (normalizedOperation) {
                case "add":
                    if (durationSeconds is > 0) {
                        _temporaryBlockedPackSuppressions = UpsertTemporarySuppressions(
                            _temporaryBlockedPackSuppressions,
                            normalizedPackIds,
                            nowTicks + TimeSpan.FromSeconds(durationSeconds.Value).Ticks,
                            _blockedPackIds,
                            StringComparer.OrdinalIgnoreCase);
                    } else {
                        _blockedPackIdsCustomized = true;
                        _blockedPackIds = MergeBlockedPackIds(_blockedPackIds, normalizedPackIds);
                        _temporaryBlockedPackSuppressions = RemoveTemporarySuppressions(_temporaryBlockedPackSuppressions, normalizedPackIds, StringComparer.OrdinalIgnoreCase);
                    }
                    break;
                case "remove":
                    _blockedPackIdsCustomized = true;
                    _blockedPackIds = RemoveBlockedPackIds(_blockedPackIds, normalizedPackIds);
                    _temporaryBlockedPackSuppressions = RemoveTemporarySuppressions(_temporaryBlockedPackSuppressions, normalizedPackIds, StringComparer.OrdinalIgnoreCase);
                    break;
                case "replace":
                    _blockedPackIdsCustomized = true;
                    _blockedPackIds = normalizedPackIds;
                    _temporaryBlockedPackSuppressions = Array.Empty<TemporarySuppression>();
                    break;
                case "clear":
                    _blockedPackIdsCustomized = true;
                    _blockedPackIds = Array.Empty<string>();
                    _temporaryBlockedPackSuppressions = Array.Empty<TemporarySuppression>();
                    break;
                case "reset":
                    _blockedPackIdsCustomized = false;
                    _blockedPackIds = _defaultBlockedPackIds;
                    _temporaryBlockedPackSuppressions = Array.Empty<TemporarySuppression>();
                    break;
            }
        }

        PersistStateNoThrow();
    }

    internal void UpdateBlockedThreads(string operation, IReadOnlyList<string>? rawThreadIds, int? durationSeconds = null) {
        var normalizedOperation = NormalizeMaintenanceWindowOperation(operation);
        var normalizedThreadIds = NormalizeBlockedThreadIds(rawThreadIds);
        var nowTicks = DateTime.UtcNow.Ticks;
        lock (_lock) {
            NormalizeExpiredTemporarySuppressionsNoLock(nowTicks);
            switch (normalizedOperation) {
                case "add":
                    if (durationSeconds is > 0) {
                        _temporaryBlockedThreadSuppressions = UpsertTemporarySuppressions(
                            _temporaryBlockedThreadSuppressions,
                            normalizedThreadIds,
                            nowTicks + TimeSpan.FromSeconds(durationSeconds.Value).Ticks,
                            _blockedThreadIds,
                            StringComparer.Ordinal);
                    } else {
                        _blockedThreadIdsCustomized = true;
                        _blockedThreadIds = MergeBlockedThreadIds(_blockedThreadIds, normalizedThreadIds);
                        _temporaryBlockedThreadSuppressions = RemoveTemporarySuppressions(_temporaryBlockedThreadSuppressions, normalizedThreadIds, StringComparer.Ordinal);
                    }
                    break;
                case "remove":
                    _blockedThreadIdsCustomized = true;
                    _blockedThreadIds = RemoveBlockedThreadIds(_blockedThreadIds, normalizedThreadIds);
                    _temporaryBlockedThreadSuppressions = RemoveTemporarySuppressions(_temporaryBlockedThreadSuppressions, normalizedThreadIds, StringComparer.Ordinal);
                    break;
                case "replace":
                    _blockedThreadIdsCustomized = true;
                    _blockedThreadIds = normalizedThreadIds;
                    _temporaryBlockedThreadSuppressions = Array.Empty<TemporarySuppression>();
                    break;
                case "clear":
                    _blockedThreadIdsCustomized = true;
                    _blockedThreadIds = Array.Empty<string>();
                    _temporaryBlockedThreadSuppressions = Array.Empty<TemporarySuppression>();
                    break;
                case "reset":
                    _blockedThreadIdsCustomized = false;
                    _blockedThreadIds = _defaultBlockedThreadIds;
                    _temporaryBlockedThreadSuppressions = Array.Empty<TemporarySuppression>();
                    break;
            }
        }

        PersistStateNoThrow();
    }

    internal bool TryResolveBlockedPackDurationUntilNextMaintenanceWindow(
        long nowTicks,
        string packId,
        out int durationSeconds,
        out string maintenanceWindowSpec) {
        lock (_lock) {
            return TryResolveDurationUntilNextMaintenanceWindowNoLock(
                nowTicks,
                normalizedThreadId: string.Empty,
                normalizedPackId: ToolPackBootstrap.NormalizePackId(packId),
                out durationSeconds,
                out maintenanceWindowSpec);
        }
    }

    internal bool TryResolveBlockedThreadDurationUntilNextMaintenanceWindow(
        long nowTicks,
        string threadId,
        out int durationSeconds,
        out string maintenanceWindowSpec) {
        lock (_lock) {
            return TryResolveDurationUntilNextMaintenanceWindowNoLock(
                nowTicks,
                normalizedThreadId: NormalizeMaintenanceWindowThreadId(threadId),
                normalizedPackId: string.Empty,
                out durationSeconds,
                out maintenanceWindowSpec);
        }
    }

    internal bool TryResolveBlockedPackDurationUntilNextMaintenanceWindowStart(
        long nowTicks,
        string packId,
        out int durationSeconds,
        out string maintenanceWindowSpec) {
        lock (_lock) {
            return TryResolveDurationUntilNextMaintenanceWindowStartNoLock(
                nowTicks,
                normalizedThreadId: string.Empty,
                normalizedPackId: ToolPackBootstrap.NormalizePackId(packId),
                out durationSeconds,
                out maintenanceWindowSpec);
        }
    }

    internal bool TryResolveBlockedThreadDurationUntilNextMaintenanceWindowStart(
        long nowTicks,
        string threadId,
        out int durationSeconds,
        out string maintenanceWindowSpec) {
        lock (_lock) {
            return TryResolveDurationUntilNextMaintenanceWindowStartNoLock(
                nowTicks,
                normalizedThreadId: NormalizeMaintenanceWindowThreadId(threadId),
                normalizedPackId: string.Empty,
                out durationSeconds,
                out maintenanceWindowSpec);
        }
    }

    internal static bool TryNormalizeMaintenanceWindowSpec(string? rawSpec, out string normalizedSpec, out string? error) {
        normalizedSpec = string.Empty;
        error = null;
        if (!TryParseMaintenanceWindow(rawSpec, out var window, out error)) {
            return false;
        }

        normalizedSpec = window.NormalizedSpec;
        return true;
    }

    internal static bool TryBuildMaintenanceWindowDto(string? rawSpec, out SessionCapabilityBackgroundSchedulerMaintenanceWindowDto dto, out string? error) {
        dto = new SessionCapabilityBackgroundSchedulerMaintenanceWindowDto();
        error = null;
        if (!TryParseMaintenanceWindow(rawSpec, out var window, out error)) {
            return false;
        }

        dto = ToMaintenanceWindowDto(window);
        return true;
    }

    internal string ResolveStorePathForTesting() {
        return ResolveStorePath();
    }

    private bool NormalizeExpiredTemporarySuppressionsNoLock(long nowTicks) {
        var changed = false;
        var normalizedPackSuppressions = RemoveExpiredTemporarySuppressions(_temporaryBlockedPackSuppressions, nowTicks);
        if (normalizedPackSuppressions.Length != _temporaryBlockedPackSuppressions.Length) {
            _temporaryBlockedPackSuppressions = normalizedPackSuppressions;
            changed = true;
        }

        var normalizedThreadSuppressions = RemoveExpiredTemporarySuppressions(_temporaryBlockedThreadSuppressions, nowTicks);
        if (normalizedThreadSuppressions.Length != _temporaryBlockedThreadSuppressions.Length) {
            _temporaryBlockedThreadSuppressions = normalizedThreadSuppressions;
            changed = true;
        }

        return changed;
    }

    private bool NormalizeExpiredManualPauseNoLock(long nowTicks) {
        if (!_manualPauseActive || _pausedUntilUtcTicks <= 0 || nowTicks <= 0 || _pausedUntilUtcTicks > nowTicks) {
            return false;
        }

        _manualPauseActive = false;
        _pausedUntilUtcTicks = 0;
        _pauseReason = string.Empty;
        return true;
    }

    private static string NormalizeActivityText(string? value, int maxLength) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (maxLength > 0 && normalized.Length > maxLength) {
            return normalized[..maxLength].TrimEnd();
        }

        return normalized;
    }

    private static string[] BuildCombinedSuppressionIdsNoLock(
        IReadOnlyList<string> persistentIds,
        IReadOnlyList<TemporarySuppression> temporarySuppressions,
        StringComparer comparer) {
        var ids = new List<string>(persistentIds.Count + temporarySuppressions.Count);
        for (var i = 0; i < persistentIds.Count; i++) {
            var id = persistentIds[i];
            if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id, comparer)) {
                ids.Add(id);
            }
        }

        for (var i = 0; i < temporarySuppressions.Count; i++) {
            var id = temporarySuppressions[i].Id;
            if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id, comparer)) {
                ids.Add(id);
            }
        }

        return ids.ToArray();
    }

    private static SessionCapabilityBackgroundSchedulerSuppressionDto[] BuildSuppressionDtosNoLock(
        IReadOnlyList<string> defaultIds,
        IReadOnlyList<string> persistentIds,
        IReadOnlyList<TemporarySuppression> temporarySuppressions,
        StringComparer comparer) {
        var dtos = new List<SessionCapabilityBackgroundSchedulerSuppressionDto>(persistentIds.Count + temporarySuppressions.Count);
        for (var i = 0; i < persistentIds.Count; i++) {
            var id = persistentIds[i];
            if (string.IsNullOrWhiteSpace(id) || dtos.Any(existing => comparer.Equals(existing.Id, id))) {
                continue;
            }

            var mode = defaultIds.Contains(id, comparer)
                ? "persistent_default"
                : "persistent_runtime";
            dtos.Add(new SessionCapabilityBackgroundSchedulerSuppressionDto {
                Id = id,
                Mode = mode,
                Temporary = false,
                ExpiresUtcTicks = 0
            });
        }

        for (var i = 0; i < temporarySuppressions.Count; i++) {
            var suppression = temporarySuppressions[i];
            if (string.IsNullOrWhiteSpace(suppression.Id) || dtos.Any(existing => comparer.Equals(existing.Id, suppression.Id))) {
                continue;
            }

            dtos.Add(new SessionCapabilityBackgroundSchedulerSuppressionDto {
                Id = suppression.Id,
                Mode = "temporary_runtime",
                Temporary = true,
                ExpiresUtcTicks = Math.Max(0, suppression.ExpiresUtcTicks)
            });
        }

        return dtos
            .OrderBy(static item => item.Temporary)
            .ThenBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private BackgroundSchedulerPauseStateSnapshot BuildSnapshotNoLock(long nowTicks) {
        if (_manualPauseActive) {
            return new BackgroundSchedulerPauseStateSnapshot(
                ManualPauseActive: true,
                ScheduledPauseActive: false,
                PausedUntilUtcTicks: _pausedUntilUtcTicks,
                PauseReason: _pauseReason);
        }

        if (TryGetActiveMaintenanceWindowPause(nowTicks, out var scheduledPause)) {
            return scheduledPause;
        }

        return new BackgroundSchedulerPauseStateSnapshot(
            ManualPauseActive: false,
            ScheduledPauseActive: false,
            PausedUntilUtcTicks: 0,
            PauseReason: string.Empty);
    }

    private bool TryGetActiveMaintenanceWindowPause(long nowTicks, out BackgroundSchedulerPauseStateSnapshot snapshot) {
        snapshot = default;
        if (_maintenanceWindows.Length == 0) {
            return false;
        }

        long bestPausedUntilUtcTicks = 0;
        string bestReason = string.Empty;
        var activeWindows = GetActiveMaintenanceWindowsNoLock(nowTicks);
        for (var i = 0; i < activeWindows.Length; i++) {
            var window = activeWindows[i];
            if (window.PackId.Length > 0 || window.ThreadId.Length > 0) {
                continue;
            }

            if (!TryResolveMaintenanceWindowPausedUntilUtcTicks(nowTicks, window, out var pausedUntilUtcTicks)) {
                continue;
            }

            if (bestPausedUntilUtcTicks == 0 || pausedUntilUtcTicks < bestPausedUntilUtcTicks) {
                bestPausedUntilUtcTicks = pausedUntilUtcTicks;
                bestReason = "maintenance_window:" + window.NormalizedSpec;
            }
        }

        if (bestPausedUntilUtcTicks <= 0) {
            return false;
        }

        snapshot = new BackgroundSchedulerPauseStateSnapshot(
            ManualPauseActive: false,
            ScheduledPauseActive: true,
            PausedUntilUtcTicks: bestPausedUntilUtcTicks,
            PauseReason: bestReason);
        return true;
    }

    private bool TryGetScopedMaintenanceWindowPauseNoLock(
        long nowTicks,
        string normalizedThreadId,
        string normalizedPackId,
        out BackgroundSchedulerPauseStateSnapshot snapshot) {
        snapshot = default;
        if (_maintenanceWindows.Length == 0) {
            return false;
        }

        var activeWindows = GetActiveMaintenanceWindowsNoLock(nowTicks);
        long bestPausedUntilUtcTicks = 0;
        string bestReason = string.Empty;
        for (var i = 0; i < activeWindows.Length; i++) {
            var window = activeWindows[i];
            if ((window.ThreadId.Length == 0 && window.PackId.Length == 0)
                || !DoesScopedMaintenanceWindowMatch(window, normalizedThreadId, normalizedPackId)) {
                continue;
            }

            if (!TryResolveMaintenanceWindowPausedUntilUtcTicks(nowTicks, window, out var pausedUntilUtcTicks)) {
                continue;
            }

            if (bestPausedUntilUtcTicks == 0 || pausedUntilUtcTicks < bestPausedUntilUtcTicks) {
                bestPausedUntilUtcTicks = pausedUntilUtcTicks;
                bestReason = "maintenance_window_scoped:" + window.NormalizedSpec;
            }
        }

        if (bestPausedUntilUtcTicks <= 0) {
            return false;
        }

        snapshot = new BackgroundSchedulerPauseStateSnapshot(
            ManualPauseActive: false,
            ScheduledPauseActive: false,
            PausedUntilUtcTicks: bestPausedUntilUtcTicks,
            PauseReason: bestReason);
        return true;
    }

    private bool TryResolveDurationUntilNextMaintenanceWindowNoLock(
        long nowTicks,
        string normalizedThreadId,
        string normalizedPackId,
        out int durationSeconds,
        out string maintenanceWindowSpec) {
        durationSeconds = 0;
        maintenanceWindowSpec = string.Empty;
        if (_maintenanceWindows.Length == 0) {
            return false;
        }

        long bestPausedUntilUtcTicks = 0;
        string bestSpec = string.Empty;
        var localNow = ResolveLocalNow(nowTicks);
        for (var i = 0; i < _maintenanceWindows.Length; i++) {
            var window = _maintenanceWindows[i];
            if (!DoesMaintenanceWindowApplyToSuppression(window, normalizedThreadId, normalizedPackId)) {
                continue;
            }

            if (!TryResolveNextMaintenanceWindowPausedUntilUtcTicks(nowTicks, window, localNow, out var pausedUntilUtcTicks)) {
                continue;
            }

            if (bestPausedUntilUtcTicks == 0 || pausedUntilUtcTicks < bestPausedUntilUtcTicks) {
                bestPausedUntilUtcTicks = pausedUntilUtcTicks;
                bestSpec = window.NormalizedSpec;
            }
        }

        if (bestPausedUntilUtcTicks <= nowTicks) {
            return false;
        }

        var durationTicks = bestPausedUntilUtcTicks - nowTicks;
        var computedSeconds = (int)Math.Ceiling(durationTicks / (double)TimeSpan.TicksPerSecond);
        durationSeconds = Math.Max(ChatRequestOptionLimits.MinPositiveTimeoutSeconds, computedSeconds);
        maintenanceWindowSpec = bestSpec;
        return true;
    }

    private bool TryResolveDurationUntilNextMaintenanceWindowStartNoLock(
        long nowTicks,
        string normalizedThreadId,
        string normalizedPackId,
        out int durationSeconds,
        out string maintenanceWindowSpec) {
        durationSeconds = 0;
        maintenanceWindowSpec = string.Empty;
        if (_maintenanceWindows.Length == 0) {
            return false;
        }

        long bestWindowStartUtcTicks = 0;
        string bestSpec = string.Empty;
        var localNow = ResolveLocalNow(nowTicks);
        for (var i = 0; i < _maintenanceWindows.Length; i++) {
            var window = _maintenanceWindows[i];
            if (!DoesMaintenanceWindowApplyToSuppression(window, normalizedThreadId, normalizedPackId)) {
                continue;
            }

            if (!TryResolveNextMaintenanceWindowStartUtcTicks(nowTicks, window, localNow, out var windowStartUtcTicks)) {
                continue;
            }

            if (bestWindowStartUtcTicks == 0 || windowStartUtcTicks < bestWindowStartUtcTicks) {
                bestWindowStartUtcTicks = windowStartUtcTicks;
                bestSpec = window.NormalizedSpec;
            }
        }

        if (bestWindowStartUtcTicks <= nowTicks) {
            return false;
        }

        var durationTicks = bestWindowStartUtcTicks - nowTicks;
        var computedSeconds = (int)Math.Ceiling(durationTicks / (double)TimeSpan.TicksPerSecond);
        durationSeconds = Math.Max(ChatRequestOptionLimits.MinPositiveTimeoutSeconds, computedSeconds);
        maintenanceWindowSpec = bestSpec;
        return true;
    }

    private MaintenanceWindow[] GetActiveMaintenanceWindowsNoLock(long nowTicks) {
        if (_maintenanceWindows.Length == 0) {
            return Array.Empty<MaintenanceWindow>();
        }

        var localNow = ResolveLocalNow(nowTicks);
        var activeWindows = new List<MaintenanceWindow>(_maintenanceWindows.Length);
        for (var i = 0; i < _maintenanceWindows.Length; i++) {
            if (TryResolveActiveMaintenanceWindowPause(_maintenanceWindows[i], localNow, out _)) {
                activeWindows.Add(_maintenanceWindows[i]);
            }
        }

        return activeWindows.ToArray();
    }

    private static DateTime ResolveLocalNow(long nowTicks) {
        DateTime utcNow;
        try {
            utcNow = nowTicks > 0
                ? new DateTime(nowTicks, DateTimeKind.Utc)
                : DateTime.UtcNow;
        } catch (ArgumentOutOfRangeException) {
            utcNow = DateTime.UtcNow;
        }

        return TimeZoneInfo.ConvertTimeFromUtc(utcNow, TimeZoneInfo.Local);
    }

    private static bool TryResolveMaintenanceWindowPausedUntilUtcTicks(long nowTicks, MaintenanceWindow window, out long pausedUntilUtcTicks) {
        var localNow = ResolveLocalNow(nowTicks);
        return TryResolveActiveMaintenanceWindowPause(window, localNow, out pausedUntilUtcTicks);
    }

    private static bool TryResolveNextMaintenanceWindowPausedUntilUtcTicks(
        long nowTicks,
        MaintenanceWindow window,
        DateTime localNow,
        out long pausedUntilUtcTicks) {
        pausedUntilUtcTicks = 0;

        if (TryResolveActiveMaintenanceWindowPause(window, localNow, out pausedUntilUtcTicks) && pausedUntilUtcTicks > nowTicks) {
            return true;
        }

        var localTodayStart = new DateTime(
            localNow.Year,
            localNow.Month,
            localNow.Day,
            0,
            0,
            0,
            DateTimeKind.Unspecified);

        for (var dayOffset = 0; dayOffset <= 7; dayOffset++) {
            var candidateDate = localTodayStart.AddDays(dayOffset);
            if ((window.DayMask & GetDayBit(candidateDate.DayOfWeek)) == 0) {
                continue;
            }

            var candidateStart = candidateDate.AddMinutes(window.StartMinuteOfDay);
            if (candidateStart <= localNow) {
                continue;
            }

            var candidateEnd = candidateStart.AddMinutes(window.DurationMinutes);
            pausedUntilUtcTicks = TimeZoneInfo.ConvertTimeToUtc(candidateEnd, TimeZoneInfo.Local).Ticks;
            return pausedUntilUtcTicks > nowTicks;
        }

        return false;
    }

    private static bool TryResolveNextMaintenanceWindowStartUtcTicks(
        long nowTicks,
        MaintenanceWindow window,
        DateTime localNow,
        out long maintenanceWindowStartUtcTicks) {
        maintenanceWindowStartUtcTicks = 0;

        var localTodayStart = new DateTime(
            localNow.Year,
            localNow.Month,
            localNow.Day,
            0,
            0,
            0,
            DateTimeKind.Unspecified);

        for (var dayOffset = 0; dayOffset <= 7; dayOffset++) {
            var candidateDate = localTodayStart.AddDays(dayOffset);
            if ((window.DayMask & GetDayBit(candidateDate.DayOfWeek)) == 0) {
                continue;
            }

            var candidateStart = candidateDate.AddMinutes(window.StartMinuteOfDay);
            if (candidateStart <= localNow) {
                continue;
            }

            maintenanceWindowStartUtcTicks = TimeZoneInfo.ConvertTimeToUtc(candidateStart, TimeZoneInfo.Local).Ticks;
            return maintenanceWindowStartUtcTicks > nowTicks;
        }

        return false;
    }

    private static bool DoesScopedMaintenanceWindowMatch(MaintenanceWindow window, string normalizedThreadId, string normalizedPackId) {
        if (window.ThreadId.Length == 0 && window.PackId.Length == 0) {
            return false;
        }

        if (window.ThreadId.Length > 0
            && !string.Equals(window.ThreadId, normalizedThreadId, StringComparison.Ordinal)) {
            return false;
        }

        if (window.PackId.Length > 0
            && !string.Equals(window.PackId, normalizedPackId, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return true;
    }

    private static bool DoesMaintenanceWindowApplyToSuppression(MaintenanceWindow window, string normalizedThreadId, string normalizedPackId) {
        if (window.ThreadId.Length == 0 && window.PackId.Length == 0) {
            return true;
        }

        return DoesScopedMaintenanceWindowMatch(window, normalizedThreadId, normalizedPackId);
    }

    private static bool TryResolveActiveMaintenanceWindowPause(MaintenanceWindow window, DateTime localNow, out long pausedUntilUtcTicks) {
        pausedUntilUtcTicks = 0;

        var localMinutes = localNow.Hour * 60 + localNow.Minute;
        var todayBit = GetDayBit(localNow.DayOfWeek);
        var previousDayBit = GetDayBit(localNow.AddDays(-1).DayOfWeek);
        var windowEndMinute = window.StartMinuteOfDay + window.DurationMinutes;

        if (windowEndMinute <= 1440) {
            if ((window.DayMask & todayBit) == 0
                || localMinutes < window.StartMinuteOfDay
                || localMinutes >= windowEndMinute) {
                return false;
            }

            var localTodayStart = new DateTime(
                localNow.Year,
                localNow.Month,
                localNow.Day,
                0,
                0,
                0,
                DateTimeKind.Unspecified);
            var localEnd = localTodayStart.AddMinutes(windowEndMinute);
            pausedUntilUtcTicks = TimeZoneInfo.ConvertTimeToUtc(localEnd, TimeZoneInfo.Local).Ticks;
            return true;
        }

        var overflowMinutes = windowEndMinute - 1440;
        if ((window.DayMask & todayBit) != 0 && localMinutes >= window.StartMinuteOfDay) {
            var localEnd = new DateTime(
                localNow.Year,
                localNow.Month,
                localNow.Day,
                0,
                0,
                0,
                DateTimeKind.Unspecified).AddDays(1).AddMinutes(overflowMinutes);
            pausedUntilUtcTicks = TimeZoneInfo.ConvertTimeToUtc(localEnd, TimeZoneInfo.Local).Ticks;
            return true;
        }

        if ((window.DayMask & previousDayBit) == 0 || localMinutes >= overflowMinutes) {
            return false;
        }

        var localCurrentDayStart = new DateTime(
            localNow.Year,
            localNow.Month,
            localNow.Day,
            0,
            0,
            0,
            DateTimeKind.Unspecified);
        pausedUntilUtcTicks = TimeZoneInfo.ConvertTimeToUtc(localCurrentDayStart.AddMinutes(overflowMinutes), TimeZoneInfo.Local).Ticks;
        return true;
    }

    private static MaintenanceWindow[] BuildMaintenanceWindows(IReadOnlyList<string>? rawSpecs) {
        if (rawSpecs is not { Count: > 0 }) {
            return Array.Empty<MaintenanceWindow>();
        }

        var windows = new List<MaintenanceWindow>(rawSpecs.Count);
        for (var i = 0; i < rawSpecs.Count; i++) {
            if (!TryParseMaintenanceWindow(rawSpecs[i], out var window, out _)) {
                continue;
            }

            if (windows.Any(existing => string.Equals(existing.NormalizedSpec, window.NormalizedSpec, StringComparison.OrdinalIgnoreCase))) {
                continue;
            }

            windows.Add(window);
        }

        return windows.ToArray();
    }

    private static bool TryParseMaintenanceWindow(string? rawSpec, out MaintenanceWindow window, out string? error) {
        window = default;
        error = null;
        var normalized = NormalizeActivityText(rawSpec, MaxMaintenanceWindowSpecLength);
        if (normalized.Length == 0) {
            error = "must use <day>@HH:mm/<minutes> optionally followed by ;pack=<id> and/or ;thread=<id>.";
            return false;
        }

        var segments = normalized.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) {
            error = "must use <day>@HH:mm/<minutes> optionally followed by ;pack=<id> and/or ;thread=<id>.";
            return false;
        }

        normalized = segments[0].ToLowerInvariant();

        var atIndex = normalized.IndexOf('@');
        var slashIndex = normalized.IndexOf('/');
        if (atIndex <= 0 || slashIndex <= atIndex + 1 || slashIndex >= normalized.Length - 1) {
            error = "must use <day>@HH:mm/<minutes> optionally followed by ;pack=<id> and/or ;thread=<id>.";
            return false;
        }

        var dayToken = normalized[..atIndex];
        var timeToken = normalized.Substring(atIndex + 1, slashIndex - atIndex - 1);
        var durationToken = normalized[(slashIndex + 1)..];

        if (!TryParseMaintenanceWindowDayMask(dayToken, out var dayMask)
            || !TryParseMaintenanceWindowTime(timeToken, out var startMinuteOfDay)
            || !int.TryParse(durationToken, out var durationMinutes)
            || durationMinutes < 1
            || durationMinutes > 1440) {
            error = "must use <day>@HH:mm/<minutes> with day in daily, mon, tue, wed, thu, fri, sat, sun and minutes in 1..1440.";
            return false;
        }

        var normalizedPackId = string.Empty;
        var normalizedThreadId = string.Empty;
        for (var i = 1; i < segments.Length; i++) {
            var segment = segments[i];
            var equalsIndex = segment.IndexOf('=');
            if (equalsIndex <= 0 || equalsIndex >= segment.Length - 1) {
                error = "scope segments must use pack=<id> or thread=<id>.";
                return false;
            }

            var key = segment[..equalsIndex].Trim().ToLowerInvariant();
            var value = segment[(equalsIndex + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0) {
                error = "scope segments must use pack=<id> or thread=<id>.";
                return false;
            }

            switch (key) {
                case "pack":
                    normalizedPackId = ToolPackBootstrap.NormalizePackId(value);
                    if (normalizedPackId.Length == 0) {
                        error = "pack scope must use a valid non-empty pack id.";
                        return false;
                    }
                    break;
                case "thread":
                    normalizedThreadId = NormalizeMaintenanceWindowThreadId(value);
                    if (normalizedThreadId.Length == 0) {
                        error = "thread scope must use a valid non-empty thread id.";
                        return false;
                    }
                    break;
                default:
                    error = "only pack=<id> and thread=<id> scope segments are supported.";
                    return false;
            }
        }

        normalized = $"{NormalizeMaintenanceWindowDayToken(dayToken)}@{startMinuteOfDay / 60:00}:{startMinuteOfDay % 60:00}/{durationMinutes}";
        if (normalizedPackId.Length > 0) {
            normalized += ";pack=" + normalizedPackId;
        }

        if (normalizedThreadId.Length > 0) {
            normalized += ";thread=" + normalizedThreadId;
        }

        window = new MaintenanceWindow(dayMask, startMinuteOfDay, durationMinutes, normalizedPackId, normalizedThreadId, normalized);
        return true;
    }

    private static string NormalizeMaintenanceWindowThreadId(string? threadId) {
        return NormalizeActivityText(threadId, MaxMaintenanceWindowThreadScopeLength);
    }

    private static SessionCapabilityBackgroundSchedulerMaintenanceWindowDto ToMaintenanceWindowDto(MaintenanceWindow window) {
        var spec = window.NormalizedSpec ?? string.Empty;
        var scheduleSegment = spec.Split(';', 2, StringSplitOptions.TrimEntries)[0];
        var atIndex = scheduleSegment.IndexOf('@');
        var slashIndex = scheduleSegment.IndexOf('/');
        var day = atIndex > 0 ? scheduleSegment[..atIndex] : string.Empty;
        var startTimeLocal = atIndex >= 0 && slashIndex > atIndex
            ? scheduleSegment.Substring(atIndex + 1, slashIndex - atIndex - 1)
            : string.Empty;

        return new SessionCapabilityBackgroundSchedulerMaintenanceWindowDto {
            Spec = spec,
            Day = day,
            StartTimeLocal = startTimeLocal,
            DurationMinutes = Math.Max(0, window.DurationMinutes),
            PackId = window.PackId,
            ThreadId = window.ThreadId,
            Scoped = window.PackId.Length > 0 || window.ThreadId.Length > 0
        };
    }

    private static bool TryParseMaintenanceWindowDayMask(string dayToken, out byte dayMask) {
        dayMask = 0;
        return NormalizeMaintenanceWindowDayToken(dayToken) switch {
            "daily" => TrySetDayMask(
                GetDayBit(DayOfWeek.Sunday)
                | GetDayBit(DayOfWeek.Monday)
                | GetDayBit(DayOfWeek.Tuesday)
                | GetDayBit(DayOfWeek.Wednesday)
                | GetDayBit(DayOfWeek.Thursday)
                | GetDayBit(DayOfWeek.Friday)
                | GetDayBit(DayOfWeek.Saturday),
                out dayMask),
            "mon" => TrySetDayMask(GetDayBit(DayOfWeek.Monday), out dayMask),
            "tue" => TrySetDayMask(GetDayBit(DayOfWeek.Tuesday), out dayMask),
            "wed" => TrySetDayMask(GetDayBit(DayOfWeek.Wednesday), out dayMask),
            "thu" => TrySetDayMask(GetDayBit(DayOfWeek.Thursday), out dayMask),
            "fri" => TrySetDayMask(GetDayBit(DayOfWeek.Friday), out dayMask),
            "sat" => TrySetDayMask(GetDayBit(DayOfWeek.Saturday), out dayMask),
            "sun" => TrySetDayMask(GetDayBit(DayOfWeek.Sunday), out dayMask),
            _ => false
        };
    }

    private static string NormalizeMaintenanceWindowDayToken(string? dayToken) {
        return (dayToken ?? string.Empty).Trim().ToLowerInvariant() switch {
            "everyday" => "daily",
            "day" => "daily",
            "monday" => "mon",
            "tuesday" => "tue",
            "wednesday" => "wed",
            "thursday" => "thu",
            "friday" => "fri",
            "saturday" => "sat",
            "sunday" => "sun",
            var value => value
        };
    }

    private static bool TryParseMaintenanceWindowTime(string timeToken, out int startMinuteOfDay) {
        startMinuteOfDay = 0;
        var parts = (timeToken ?? string.Empty).Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var hour)
            || !int.TryParse(parts[1], out var minute)
            || hour < 0
            || hour > 23
            || minute < 0
            || minute > 59) {
            return false;
        }

        startMinuteOfDay = hour * 60 + minute;
        return true;
    }

    private static bool TrySetDayMask(int value, out byte dayMask) {
        dayMask = (byte)value;
        return true;
    }

    private static byte GetDayBit(DayOfWeek dayOfWeek) {
        return dayOfWeek switch {
            DayOfWeek.Monday => 1 << 0,
            DayOfWeek.Tuesday => 1 << 1,
            DayOfWeek.Wednesday => 1 << 2,
            DayOfWeek.Thursday => 1 << 3,
            DayOfWeek.Friday => 1 << 4,
            DayOfWeek.Saturday => 1 << 5,
            _ => 1 << 6
        };
    }

    private static string NormalizeMaintenanceWindowOperation(string? operation) {
        return (operation ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string[] NormalizeMaintenanceWindowSpecs(IReadOnlyList<string>? rawSpecs) {
        if (rawSpecs is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(rawSpecs.Count);
        for (var i = 0; i < rawSpecs.Count; i++) {
            if (!TryNormalizeMaintenanceWindowSpec(rawSpecs[i], out var normalizedSpec, out _)) {
                continue;
            }

            if (normalized.Any(existing => string.Equals(existing, normalizedSpec, StringComparison.OrdinalIgnoreCase))) {
                continue;
            }

            normalized.Add(normalizedSpec);
        }

        return normalized.ToArray();
    }

    private static MaintenanceWindow[] MergeMaintenanceWindows(MaintenanceWindow[] existing, string[] rawSpecs) {
        if (rawSpecs.Length == 0) {
            return existing;
        }

        var current = existing
            .Select(static item => item.NormalizedSpec)
            .ToList();
        for (var i = 0; i < rawSpecs.Length; i++) {
            if (current.Any(existingSpec => string.Equals(existingSpec, rawSpecs[i], StringComparison.OrdinalIgnoreCase))) {
                continue;
            }

            current.Add(rawSpecs[i]);
        }

        return BuildMaintenanceWindows(current);
    }

    private static MaintenanceWindow[] RemoveMaintenanceWindows(MaintenanceWindow[] existing, string[] rawSpecs) {
        if (existing.Length == 0 || rawSpecs.Length == 0) {
            return existing;
        }

        var filtered = existing
            .Select(static item => item.NormalizedSpec)
            .Where(spec => !rawSpecs.Any(removeSpec => string.Equals(removeSpec, spec, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        return BuildMaintenanceWindows(filtered);
    }

    private static string[] NormalizeBlockedThreadIds(IReadOnlyList<string>? rawThreadIds) {
        if (rawThreadIds is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var list = new List<string>(rawThreadIds.Count);
        for (var i = 0; i < rawThreadIds.Count; i++) {
            var normalized = NormalizeMaintenanceWindowThreadId(rawThreadIds[i]);
            if (normalized.Length == 0 || list.Contains(normalized, StringComparer.Ordinal)) {
                continue;
            }

            list.Add(normalized);
        }

        return list.ToArray();
    }

    private static string[] NormalizeBlockedPackIds(IReadOnlyList<string>? rawPackIds) {
        if (rawPackIds is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var list = new List<string>(rawPackIds.Count);
        for (var i = 0; i < rawPackIds.Count; i++) {
            var normalized = ToolPackBootstrap.NormalizePackId(rawPackIds[i]);
            if (normalized.Length == 0 || list.Contains(normalized, StringComparer.OrdinalIgnoreCase)) {
                continue;
            }

            list.Add(normalized);
        }

        return list.ToArray();
    }

    private static TemporarySuppression[] NormalizeTemporarySuppressions(
        IReadOnlyList<TemporarySuppressionStoreDto>? rawSuppressions,
        int maxIdLength,
        Func<string?, string> normalizeId,
        StringComparer comparer,
        long nowTicks) {
        if (rawSuppressions is not { Count: > 0 }) {
            return Array.Empty<TemporarySuppression>();
        }

        var list = new List<TemporarySuppression>(rawSuppressions.Count);
        for (var i = 0; i < rawSuppressions.Count; i++) {
            var entry = rawSuppressions[i];
            if (entry is null) {
                continue;
            }

            var normalizedId = NormalizeActivityText(normalizeId(entry.Id), maxIdLength);
            if (normalizedId.Length == 0 || entry.ExpiresUtcTicks <= nowTicks) {
                continue;
            }

            var existingIndex = list.FindIndex(existing => comparer.Equals(existing.Id, normalizedId));
            if (existingIndex >= 0) {
                if (list[existingIndex].ExpiresUtcTicks < entry.ExpiresUtcTicks) {
                    list[existingIndex] = new TemporarySuppression(normalizedId, Math.Max(0, entry.ExpiresUtcTicks));
                }
                continue;
            }

            list.Add(new TemporarySuppression(normalizedId, Math.Max(0, entry.ExpiresUtcTicks)));
        }

        return list.ToArray();
    }

    private static TemporarySuppression[] RemoveExpiredTemporarySuppressions(
        IReadOnlyList<TemporarySuppression> suppressions,
        long nowTicks) {
        if (suppressions.Count == 0) {
            return Array.Empty<TemporarySuppression>();
        }

        return suppressions
            .Where(suppression => suppression.ExpiresUtcTicks > nowTicks)
            .ToArray();
    }

    private static TemporarySuppression[] UpsertTemporarySuppressions(
        IReadOnlyList<TemporarySuppression> existing,
        IReadOnlyList<string> ids,
        long expiresUtcTicks,
        IReadOnlyList<string> persistentIds,
        StringComparer comparer) {
        if (ids.Count == 0 || expiresUtcTicks <= 0) {
            return existing.Count == 0 ? Array.Empty<TemporarySuppression>() : existing.ToArray();
        }

        var list = existing.ToList();
        for (var i = 0; i < ids.Count; i++) {
            var id = ids[i];
            if (string.IsNullOrWhiteSpace(id) || persistentIds.Contains(id, comparer)) {
                continue;
            }

            var existingIndex = list.FindIndex(entry => comparer.Equals(entry.Id, id));
            var suppression = new TemporarySuppression(id, expiresUtcTicks);
            if (existingIndex >= 0) {
                list[existingIndex] = suppression;
            } else {
                list.Add(suppression);
            }
        }

        return list.ToArray();
    }

    private static TemporarySuppression[] RemoveTemporarySuppressions(
        IReadOnlyList<TemporarySuppression> existing,
        IReadOnlyList<string> ids,
        StringComparer comparer) {
        if (existing.Count == 0 || ids.Count == 0) {
            return existing.Count == 0 ? Array.Empty<TemporarySuppression>() : existing.ToArray();
        }

        return existing
            .Where(entry => !ids.Contains(entry.Id, comparer))
            .ToArray();
    }

    private static string[] MergeBlockedPackIds(IReadOnlyList<string> existing, IReadOnlyList<string> added) {
        if (added.Count == 0) {
            return existing.Count == 0 ? Array.Empty<string>() : existing.ToArray();
        }

        var list = new List<string>(existing.Count + added.Count);
        for (var i = 0; i < existing.Count; i++) {
            if (!list.Contains(existing[i], StringComparer.OrdinalIgnoreCase)) {
                list.Add(existing[i]);
            }
        }

        for (var i = 0; i < added.Count; i++) {
            if (!list.Contains(added[i], StringComparer.OrdinalIgnoreCase)) {
                list.Add(added[i]);
            }
        }

        return list.ToArray();
    }

    private static string[] RemoveBlockedPackIds(IReadOnlyList<string> existing, IReadOnlyList<string> removed) {
        if (existing.Count == 0 || removed.Count == 0) {
            return existing.Count == 0 ? Array.Empty<string>() : existing.ToArray();
        }

        var list = new List<string>(existing.Count);
        for (var i = 0; i < existing.Count; i++) {
            if (!removed.Contains(existing[i], StringComparer.OrdinalIgnoreCase)) {
                list.Add(existing[i]);
            }
        }

        return list.ToArray();
    }

    private static string[] MergeBlockedThreadIds(IReadOnlyList<string> existing, IReadOnlyList<string> added) {
        if (added.Count == 0) {
            return existing.Count == 0 ? Array.Empty<string>() : existing.ToArray();
        }

        var list = new List<string>(existing.Count + added.Count);
        for (var i = 0; i < existing.Count; i++) {
            if (!list.Contains(existing[i], StringComparer.Ordinal)) {
                list.Add(existing[i]);
            }
        }

        for (var i = 0; i < added.Count; i++) {
            if (!list.Contains(added[i], StringComparer.Ordinal)) {
                list.Add(added[i]);
            }
        }

        return list.ToArray();
    }

    private static string[] RemoveBlockedThreadIds(IReadOnlyList<string> existing, IReadOnlyList<string> removed) {
        if (existing.Count == 0 || removed.Count == 0) {
            return existing.Count == 0 ? Array.Empty<string>() : existing.ToArray();
        }

        var list = new List<string>(existing.Count);
        for (var i = 0; i < existing.Count; i++) {
            if (!removed.Contains(existing[i], StringComparer.Ordinal)) {
                list.Add(existing[i]);
            }
        }

        return list.ToArray();
    }

    private string ResolveStorePath() {
        var pendingActionsPath = ResolvePendingActionsStorePath();
        var directory = Path.GetDirectoryName(pendingActionsPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            return Path.Combine(directory, "background-scheduler-control.json");
        }

        return Path.Combine(Environment.CurrentDirectory, "background-scheduler-control.json");
    }

    private string ResolvePendingActionsStorePath() {
        var candidate = (_options.PendingActionsStorePath ?? string.Empty).Trim();
        if (candidate.Length == 0) {
            return ResolveDefaultPendingActionsStorePath();
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        var baseDir = Path.Combine(root, "IntelligenceX.Chat");
        var defaultPath = ResolveDefaultPendingActionsStorePath();

        try {
            if (candidate.StartsWith(@"\\", StringComparison.Ordinal)) {
                return defaultPath;
            }

            if (!Path.IsPathFullyQualified(candidate)) {
                if (candidate.Contains("..", StringComparison.Ordinal)
                    || candidate.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0) {
                    return defaultPath;
                }

                return Path.Combine(baseDir, candidate);
            }

            var fullCandidate = Path.GetFullPath(candidate);
            var fullBaseDir = Path.GetFullPath(baseDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            return fullCandidate.StartsWith(fullBaseDir, StringComparison.OrdinalIgnoreCase)
                ? fullCandidate
                : defaultPath;
        } catch {
            return defaultPath;
        }
    }

    private static string ResolveDefaultPendingActionsStorePath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", "pending-actions.json");
    }

    private StoreDto ReadStoreStateNoThrow() {
        var path = ResolveStorePath();
        lock (StoreLock) {
            try {
                if (!File.Exists(path)) {
                    return new StoreDto();
                }

                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > 64 * 1024) {
                    return new StoreDto();
                }

                var json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) {
                    return new StoreDto();
                }

                var store = JsonSerializer.Deserialize<StoreDto>(json, StoreJsonOptions);
                if (store is null || store.Version <= 0 || store.Version > StoreVersion) {
                    return new StoreDto();
                }

                var normalized = new StoreDto {
                    ManualPauseActive = store.ManualPauseActive,
                    PausedUntilUtcTicks = Math.Max(0, store.PausedUntilUtcTicks),
                    PauseReason = NormalizeActivityText(store.PauseReason, MaxPauseReasonLength + 32),
                    BlockedPackIdsCustomized = store.BlockedPackIdsCustomized,
                    BlockedPackIds = NormalizeBlockedPackIds(store.BlockedPackIds),
                    TemporaryBlockedPacks = NormalizeTemporarySuppressions(
                            store.TemporaryBlockedPacks,
                            maxIdLength: ChatRequestOptionLimits.MaxToolSelectorLength,
                            normalizeId: ToolPackBootstrap.NormalizePackId,
                            StringComparer.OrdinalIgnoreCase,
                            DateTime.UtcNow.Ticks)
                        .Select(static item => new TemporarySuppressionStoreDto {
                            Id = item.Id,
                            ExpiresUtcTicks = item.ExpiresUtcTicks
                        })
                        .ToArray(),
                    BlockedThreadIdsCustomized = store.BlockedThreadIdsCustomized,
                    BlockedThreadIds = NormalizeBlockedThreadIds(store.BlockedThreadIds),
                    TemporaryBlockedThreads = NormalizeTemporarySuppressions(
                            store.TemporaryBlockedThreads,
                            maxIdLength: ChatRequestOptionLimits.MaxToolSelectorLength,
                            normalizeId: NormalizeMaintenanceWindowThreadId,
                            StringComparer.Ordinal,
                            DateTime.UtcNow.Ticks)
                        .Select(static item => new TemporarySuppressionStoreDto {
                            Id = item.Id,
                            ExpiresUtcTicks = item.ExpiresUtcTicks
                        })
                        .ToArray(),
                    MaintenanceWindowsCustomized = store.MaintenanceWindowsCustomized,
                    MaintenanceWindowSpecs = NormalizeMaintenanceWindowSpecs(store.MaintenanceWindowSpecs)
                };

                if (!normalized.ManualPauseActive) {
                    normalized.PausedUntilUtcTicks = 0;
                    normalized.PauseReason = string.Empty;
                }

                if (normalized.PausedUntilUtcTicks > 0 && (!TryGetUtcDateTimeFromTicks(normalized.PausedUntilUtcTicks, out var pausedUntilUtc) || pausedUntilUtc <= DateTime.UtcNow)) {
                    TryDeleteStoreNoThrow(path);
                    return new StoreDto {
                        BlockedPackIdsCustomized = normalized.BlockedPackIdsCustomized,
                        BlockedPackIds = normalized.BlockedPackIds,
                        TemporaryBlockedPacks = normalized.TemporaryBlockedPacks,
                        BlockedThreadIdsCustomized = normalized.BlockedThreadIdsCustomized,
                        BlockedThreadIds = normalized.BlockedThreadIds,
                        TemporaryBlockedThreads = normalized.TemporaryBlockedThreads,
                        MaintenanceWindowsCustomized = normalized.MaintenanceWindowsCustomized,
                        MaintenanceWindowSpecs = normalized.MaintenanceWindowSpecs
                    };
                }

                return normalized;
            } catch (Exception ex) {
                Trace.TraceWarning($"Background scheduler control state read failed: {ex.GetType().Name}: {ex.Message}");
                return new StoreDto();
            }
        }
    }

    private void PersistStateNoThrow() {
        bool manualPauseActive;
        long pausedUntilUtcTicks;
        string pauseReason;
        bool blockedPackIdsCustomized;
        string[] blockedPackIds;
        TemporarySuppressionStoreDto[] temporaryBlockedPacks;
        bool blockedThreadIdsCustomized;
        string[] blockedThreadIds;
        TemporarySuppressionStoreDto[] temporaryBlockedThreads;
        bool maintenanceWindowsCustomized;
        string[] maintenanceWindowSpecs;
        lock (_lock) {
            var nowTicks = DateTime.UtcNow.Ticks;
            NormalizeExpiredTemporarySuppressionsNoLock(nowTicks);
            manualPauseActive = _manualPauseActive;
            pausedUntilUtcTicks = _pausedUntilUtcTicks;
            pauseReason = _pauseReason;
            blockedPackIdsCustomized = _blockedPackIdsCustomized;
            blockedPackIds = _blockedPackIds.ToArray();
            temporaryBlockedPacks = _temporaryBlockedPackSuppressions
                .Select(static item => new TemporarySuppressionStoreDto {
                    Id = item.Id,
                    ExpiresUtcTicks = item.ExpiresUtcTicks
                })
                .ToArray();
            blockedThreadIdsCustomized = _blockedThreadIdsCustomized;
            blockedThreadIds = _blockedThreadIds.ToArray();
            temporaryBlockedThreads = _temporaryBlockedThreadSuppressions
                .Select(static item => new TemporarySuppressionStoreDto {
                    Id = item.Id,
                    ExpiresUtcTicks = item.ExpiresUtcTicks
                })
                .ToArray();
            maintenanceWindowsCustomized = _maintenanceWindowsCustomized;
            maintenanceWindowSpecs = _maintenanceWindows
                .Select(static window => window.NormalizedSpec)
                .ToArray();
        }

        var path = ResolveStorePath();
        lock (StoreLock) {
            try {
                if (!manualPauseActive
                    && !blockedPackIdsCustomized
                    && temporaryBlockedPacks.Length == 0
                    && !blockedThreadIdsCustomized
                    && temporaryBlockedThreads.Length == 0
                    && !maintenanceWindowsCustomized) {
                    TryDeleteStoreNoThrow(path);
                    return;
                }

                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }

                var store = new StoreDto {
                    ManualPauseActive = manualPauseActive,
                    PausedUntilUtcTicks = manualPauseActive ? Math.Max(0, pausedUntilUtcTicks) : 0,
                    PauseReason = manualPauseActive ? NormalizeActivityText(pauseReason, MaxPauseReasonLength + 32) : string.Empty,
                    BlockedPackIdsCustomized = blockedPackIdsCustomized,
                    BlockedPackIds = blockedPackIdsCustomized ? blockedPackIds : Array.Empty<string>(),
                    TemporaryBlockedPacks = temporaryBlockedPacks,
                    BlockedThreadIdsCustomized = blockedThreadIdsCustomized,
                    BlockedThreadIds = blockedThreadIdsCustomized ? blockedThreadIds : Array.Empty<string>(),
                    TemporaryBlockedThreads = temporaryBlockedThreads,
                    MaintenanceWindowsCustomized = maintenanceWindowsCustomized,
                    MaintenanceWindowSpecs = maintenanceWindowsCustomized ? maintenanceWindowSpecs : Array.Empty<string>()
                };
                var json = JsonSerializer.Serialize(store, StoreJsonOptions);
                string? tmp = null;
                try {
                    var fileName = Path.GetFileName(path);
                    var tmpName = $"{fileName}.{Guid.NewGuid():N}.tmp";
                    tmp = string.IsNullOrWhiteSpace(directory) ? tmpName : Path.Combine(directory!, tmpName);

                    using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                        TryHardenStoreAclNoThrow(tmp);
                        using var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                        writer.Write(json);
                        writer.Flush();
                        fs.Flush(true);
                    }

                    if (File.Exists(path)) {
                        File.Replace(tmp, path, null, ignoreMetadataErrors: true);
                    } else {
                        File.Move(tmp, path);
                    }

                    TryHardenStoreAclNoThrow(path);
                } finally {
                    if (!string.IsNullOrWhiteSpace(tmp) && File.Exists(tmp)) {
                        try {
                            File.Delete(tmp);
                        } catch {
                            // Best effort only.
                        }
                    }
                }
            } catch (Exception ex) {
                Trace.TraceWarning($"Background scheduler control state write failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static void TryDeleteStoreNoThrow(string path) {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        } catch (Exception ex) {
            Trace.TraceWarning($"Background scheduler control state clear failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool TryGetUtcDateTimeFromTicks(long utcTicks, out DateTime value) {
        value = default;
        if (utcTicks <= 0 || utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks) {
            return false;
        }

        try {
            value = new DateTime(utcTicks, DateTimeKind.Utc);
            return true;
        } catch (ArgumentOutOfRangeException) {
            return false;
        }
    }

    private static void TryHardenStoreAclNoThrow(string path) {
        if (string.IsNullOrWhiteSpace(path) || !OperatingSystem.IsWindows()) {
            return;
        }

        try {
            if (!File.Exists(path)) {
                return;
            }

            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.Directory) != 0) {
                return;
            }

            var currentSid = WindowsIdentity.GetCurrent().User;
            if (currentSid is null) {
                return;
            }

            var fileInfo = new FileInfo(path);
            var security = fileInfo.GetAccessControl();
            security.SetOwner(currentSid);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
            security.SetAccessRule(new FileSystemAccessRule(currentSid, FileSystemRights.FullControl, AccessControlType.Allow));
            fileInfo.SetAccessControl(security);
        } catch (Exception ex) {
            Trace.TraceWarning($"Background scheduler control state ACL hardening failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal readonly record struct BackgroundSchedulerPauseStateSnapshot(
        bool ManualPauseActive,
        bool ScheduledPauseActive,
        long PausedUntilUtcTicks,
        string PauseReason);
}
