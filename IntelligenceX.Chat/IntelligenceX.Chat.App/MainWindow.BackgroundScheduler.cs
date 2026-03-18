using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.Client;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private sealed record BackgroundSchedulerContinuationPlan {
        public string ThreadId { get; init; } = string.Empty;
        public string NextAction { get; init; } = string.Empty;
        public string RecoveryReason { get; init; } = string.Empty;
        public bool RefreshServiceProfiles { get; init; }
        public bool ApplyServiceProfile { get; init; }
        public bool RefreshSchedulerThread { get; init; }
        public string? ProfileName { get; init; }
        public string[] MissingArgumentNames { get; init; } = Array.Empty<string>();
        public string StatusSummary { get; init; } = string.Empty;
    }

    private static void ValidateBackgroundSchedulerMaintenanceWindowScope(string? packId, string? threadId) {
        var hasPackId = !string.IsNullOrWhiteSpace(packId);
        var hasThreadId = !string.IsNullOrWhiteSpace(threadId);
        if (hasPackId && hasThreadId) {
            throw new ArgumentException("Choose either packId or threadId for a maintenance window scope, not both.", nameof(threadId));
        }
    }

    internal static object? BuildBackgroundSchedulerContinuationPlan(
        SessionCapabilityBackgroundSchedulerDto? scheduler,
        string? threadId,
        string? appProfileName,
        string[]? serviceProfileNames,
        string? activeServiceProfileName) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0
            || scheduler?.ThreadSummaries is not { Length: > 0 } threadSummaries) {
            return null;
        }

        SessionCapabilityBackgroundSchedulerThreadSummaryDto? threadSummary = null;
        for (var i = 0; i < threadSummaries.Length; i++) {
            var candidate = threadSummaries[i];
            if (candidate is not null
                && string.Equals((candidate.ThreadId ?? string.Empty).Trim(), normalizedThreadId, StringComparison.OrdinalIgnoreCase)) {
                threadSummary = candidate;
                break;
            }
        }

        var continuationHint = threadSummary?.ContinuationHint;
        if (continuationHint is null) {
            return null;
        }

        var normalizedAppProfileName = (appProfileName ?? string.Empty).Trim();
        var normalizedServiceProfileNames = NormalizeProfileNames(serviceProfileNames);
        var normalizedActiveServiceProfileName = (activeServiceProfileName ?? string.Empty).Trim();
        var nextAction = (continuationHint.NextAction ?? string.Empty).Trim();
        var recoveryReason = (continuationHint.RecoveryReason ?? string.Empty).Trim();
        var requestKinds = (continuationHint.SuggestedRequests ?? Array.Empty<SessionCapabilityBackgroundSchedulerContinuationRequestDto>())
            .Select(static request => (request.RequestKind ?? string.Empty).Trim())
            .Where(static requestKind => requestKind.Length > 0)
            .ToArray();

        var hasListProfilesRequest = Array.Exists(requestKinds, static requestKind => string.Equals(requestKind, "list_profiles", StringComparison.OrdinalIgnoreCase));
        var hasSetProfileRequest = Array.Exists(requestKinds, static requestKind => string.Equals(requestKind, "set_profile", StringComparison.OrdinalIgnoreCase));
        var hasSchedulerRefreshRequest = Array.Exists(requestKinds, static requestKind => string.Equals(requestKind, "get_background_scheduler_status", StringComparison.OrdinalIgnoreCase));

        if (hasSetProfileRequest) {
            if (normalizedServiceProfileNames.Length == 0 && hasListProfilesRequest) {
                return new BackgroundSchedulerContinuationPlan {
                    ThreadId = normalizedThreadId,
                    NextAction = nextAction,
                    RecoveryReason = recoveryReason,
                    RefreshServiceProfiles = true,
                    StatusSummary = "Refresh service profiles before applying runtime auth context for blocked thread '" + normalizedThreadId + "'."
                };
            }

            if (normalizedAppProfileName.Length == 0 || !ContainsProfileName(normalizedServiceProfileNames, normalizedAppProfileName)) {
                return new BackgroundSchedulerContinuationPlan {
                    ThreadId = normalizedThreadId,
                    NextAction = nextAction,
                    RecoveryReason = recoveryReason,
                    MissingArgumentNames = new[] { "profileName" },
                    StatusSummary = "Blocked thread '" + normalizedThreadId + "' needs a saved app profile before runtime auth continuation can run."
                };
            }

            if (string.Equals(normalizedActiveServiceProfileName, normalizedAppProfileName, StringComparison.OrdinalIgnoreCase)) {
                return new BackgroundSchedulerContinuationPlan {
                    ThreadId = normalizedThreadId,
                    NextAction = nextAction,
                    RecoveryReason = recoveryReason,
                    RefreshSchedulerThread = true,
                    ProfileName = normalizedAppProfileName,
                    StatusSummary = "Refresh blocked thread '" + normalizedThreadId + "' after confirming runtime profile '" + normalizedAppProfileName + "'."
                };
            }

            return new BackgroundSchedulerContinuationPlan {
                ThreadId = normalizedThreadId,
                NextAction = nextAction,
                RecoveryReason = recoveryReason,
                ApplyServiceProfile = true,
                RefreshSchedulerThread = true,
                ProfileName = normalizedAppProfileName,
                StatusSummary = "Apply runtime profile '" + normalizedAppProfileName + "' and refresh blocked thread '" + normalizedThreadId + "'."
            };
        }

        if (hasSchedulerRefreshRequest) {
            return new BackgroundSchedulerContinuationPlan {
                ThreadId = normalizedThreadId,
                NextAction = nextAction,
                RecoveryReason = recoveryReason,
                RefreshSchedulerThread = true,
                StatusSummary = "Refresh blocked thread '" + normalizedThreadId + "' to continue background scheduler recovery."
            };
        }

        return new BackgroundSchedulerContinuationPlan {
            ThreadId = normalizedThreadId,
            NextAction = nextAction,
            RecoveryReason = recoveryReason,
            StatusSummary = continuationHint.StatusSummary
        };
    }

    private BackgroundSchedulerContinuationPlan? ResolveBackgroundSchedulerContinuationPlan(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return null;
        }

        var snapshots = new[] {
            _backgroundSchedulerScopedStatusSnapshot,
            _backgroundSchedulerStatusSnapshot,
            _backgroundSchedulerGlobalStatusSnapshot,
            _sessionPolicy?.CapabilitySnapshot?.BackgroundScheduler,
            _toolCatalogCapabilitySnapshot?.BackgroundScheduler
        };

        for (var i = 0; i < snapshots.Length; i++) {
            if (BuildBackgroundSchedulerContinuationPlan(
                    snapshots[i],
                    normalizedThreadId,
                    _appProfileName,
                    _serviceProfileNames,
                    _serviceActiveProfileName) is BackgroundSchedulerContinuationPlan plan) {
                return plan;
            }
        }

        return null;
    }

    private void ApplyBackgroundSchedulerSnapshot(SessionCapabilityBackgroundSchedulerDto? scheduler, bool scoped) {
        if (scoped) {
            _backgroundSchedulerScopedStatusSnapshot = scheduler;
            return;
        }

        _backgroundSchedulerScopedStatusSnapshot = null;
        _backgroundSchedulerStatusSnapshot = scheduler;
        _backgroundSchedulerGlobalStatusSnapshot = scheduler;
    }

    private void SeedBackgroundSchedulerSnapshot(SessionCapabilityBackgroundSchedulerDto? scheduler) {
        if (scheduler is null) {
            return;
        }

        _backgroundSchedulerStatusSnapshot ??= scheduler;
        _backgroundSchedulerGlobalStatusSnapshot ??= scheduler;
    }

    private void ClearBackgroundSchedulerSnapshots() {
        _backgroundSchedulerStatusSnapshot = null;
        _backgroundSchedulerScopedStatusSnapshot = null;
        _backgroundSchedulerGlobalStatusSnapshot = null;
    }

    private void RestoreBackgroundSchedulerSnapshotAfterRefreshFailure(bool scopedRefresh) {
        if (scopedRefresh) {
            return;
        }

        if (_backgroundSchedulerGlobalStatusSnapshot is not null) {
            _backgroundSchedulerStatusSnapshot = _backgroundSchedulerGlobalStatusSnapshot;
        }
    }

    private async Task RefreshBackgroundSchedulerFromUiAsync(string? threadId) {
        if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
            return;
        }

        var client = _client;
        if (client is null) {
            return;
        }

        await RefreshBackgroundSchedulerStatusAsync(
            client,
            publishOptions: true,
            appendWarnings: true,
            threadId: threadId,
            includeRecentActivity: true,
            includeThreadSummaries: true,
            maxRecentActivity: 8,
            maxThreadSummaries: 8).ConfigureAwait(false);
        var refreshedSnapshot = string.IsNullOrWhiteSpace(threadId)
            ? _backgroundSchedulerStatusSnapshot
            : _backgroundSchedulerScopedStatusSnapshot;
        if (refreshedSnapshot is not null) {
            await SetStatusAsync("Background scheduler refreshed. " + BuildBackgroundSchedulerSummaryText(refreshedSnapshot)).ConfigureAwait(false);
        }
    }

    private async Task ContinueBackgroundSchedulerThreadFromUiAsync(string? threadId) {
        if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
            return;
        }

        var client = _client;
        if (client is null) {
            return;
        }

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            await SetStatusAsync("Background scheduler continuation failed: threadId must be provided.", SessionStatusTone.Warn).ConfigureAwait(false);
            return;
        }

        try {
            var plan = ResolveBackgroundSchedulerContinuationPlan(normalizedThreadId);
            if (plan is null) {
                await SetStatusAsync("Background scheduler continuation failed: no continuation hint is available for thread '" + normalizedThreadId + "'.", SessionStatusTone.Warn).ConfigureAwait(false);
                return;
            }

            if (plan.RefreshServiceProfiles) {
                await RefreshServiceProfilesAsync(client, publishOptions: false, appendWarnings: true).ConfigureAwait(false);
                plan = ResolveBackgroundSchedulerContinuationPlan(normalizedThreadId);
                if (plan is null) {
                    await SetStatusAsync("Background scheduler continuation failed: no continuation hint is available for thread '" + normalizedThreadId + "' after refreshing profiles.", SessionStatusTone.Warn).ConfigureAwait(false);
                    return;
                }
            }

            if (plan.MissingArgumentNames.Length > 0) {
                await PublishOptionsStateAsync().ConfigureAwait(false);
                await SetStatusAsync(
                    "Background scheduler continuation is blocked for thread '" + normalizedThreadId + "': missing " + string.Join(", ", plan.MissingArgumentNames) + ".",
                    SessionStatusTone.Warn).ConfigureAwait(false);
                return;
            }

            if (plan.ApplyServiceProfile) {
                using var setProfileCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                _ = await client.SetProfileAsync(plan.ProfileName ?? string.Empty, newThread: false, setProfileCts.Token).ConfigureAwait(false);
                _serviceActiveProfileName = plan.ProfileName;
            }

            if (plan.RefreshSchedulerThread) {
                await RefreshBackgroundSchedulerStatusAsync(
                    client,
                    publishOptions: true,
                    appendWarnings: true,
                    threadId: normalizedThreadId,
                    includeRecentActivity: true,
                    includeThreadSummaries: true,
                    maxRecentActivity: 8,
                    maxThreadSummaries: 8).ConfigureAwait(false);
                var refreshedSnapshot = _backgroundSchedulerScopedStatusSnapshot ?? _backgroundSchedulerStatusSnapshot;
                if (refreshedSnapshot is not null) {
                    await SetStatusAsync("Background scheduler continuation completed. " + BuildBackgroundSchedulerSummaryText(refreshedSnapshot)).ConfigureAwait(false);
                    return;
                }
            } else {
                await PublishOptionsStateAsync().ConfigureAwait(false);
            }

            await SetStatusAsync(plan.StatusSummary.Length > 0
                ? plan.StatusSummary
                : "Background scheduler continuation completed for thread '" + normalizedThreadId + "'.").ConfigureAwait(false);
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                AppendSystem("Couldn't continue blocked background scheduler thread '" + normalizedThreadId + "': " + ex.Message);
            }
            await SetStatusAsync("Background scheduler continuation failed: " + ex.Message, SessionStatusTone.Warn).ConfigureAwait(false);
        }
    }

    private async Task SetBackgroundSchedulerPausedFromUiAsync(bool paused, string? pauseMinutesText, string? reason) {
        if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
            return;
        }

        var client = _client;
        if (client is null) {
            return;
        }

        int? pauseSeconds = null;
        if (paused) {
            try {
                var pauseMinutes = ParseSchedulerPositiveInt(pauseMinutesText, min: 1, max: 1440, nameof(pauseMinutesText));
                pauseSeconds = checked(pauseMinutes * 60);
            } catch (Exception ex) {
                await SetStatusAsync("Background scheduler update failed: " + ex.Message, SessionStatusTone.Warn).ConfigureAwait(false);
                return;
            }
        }

        var result = await InvokeBackgroundSchedulerMutationAsync(
            static (serviceClient, cancellationToken, state) => serviceClient.SetBackgroundSchedulerPausedAsync(
                state.Paused,
                state.PauseSeconds,
                state.Reason,
                cancellationToken),
            (Paused: paused, PauseSeconds: pauseSeconds, Reason: reason),
            paused ? "Background scheduler paused." : "Background scheduler resumed.").ConfigureAwait(false);
        if (!paused && result?.Scheduler is { } scheduler && !scheduler.Paused) {
            await SetStatusAsync("Background scheduler resumed. " + BuildBackgroundSchedulerSummaryText(scheduler)).ConfigureAwait(false);
        }
    }

    private async Task AddBackgroundSchedulerMaintenanceWindowFromUiAsync(
        string? day,
        string? startTimeLocal,
        string? durationMinutesText,
        string? packId,
        string? threadId) {
        if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
            return;
        }

        var client = _client;
        if (client is null) {
            return;
        }

        string spec;
        try {
            var durationMinutes = ParseSchedulerPositiveInt(durationMinutesText, min: 1, max: 1440, nameof(durationMinutesText));
            ValidateBackgroundSchedulerMaintenanceWindowScope(packId, threadId);
            spec = ChatServiceClient.BuildBackgroundSchedulerMaintenanceWindowSpec(
                day ?? "daily",
                startTimeLocal ?? string.Empty,
                durationMinutes,
                packId,
                threadId);
        } catch (Exception ex) {
            await SetStatusAsync("Background scheduler update failed: " + ex.Message, SessionStatusTone.Warn).ConfigureAwait(false);
            return;
        }

        await InvokeBackgroundSchedulerMutationAsync(
            static (serviceClient, cancellationToken, maintenanceSpec) => serviceClient.SetBackgroundSchedulerMaintenanceWindowsAsync(
                "add",
                new[] { maintenanceSpec },
                cancellationToken),
            spec,
            "Background scheduler maintenance window added.").ConfigureAwait(false);
    }

    private async Task RemoveBackgroundSchedulerMaintenanceWindowFromUiAsync(string? spec) {
        if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
            return;
        }

        var client = _client;
        if (client is null) {
            return;
        }

        var normalizedSpec = (spec ?? string.Empty).Trim();
        if (normalizedSpec.Length == 0) {
            await SetStatusAsync("Background scheduler update failed: spec must be provided.", SessionStatusTone.Warn).ConfigureAwait(false);
            return;
        }

        await InvokeBackgroundSchedulerMutationAsync(
            static (serviceClient, cancellationToken, maintenanceSpec) => serviceClient.SetBackgroundSchedulerMaintenanceWindowsAsync(
                "remove",
                new[] { maintenanceSpec },
                cancellationToken),
            normalizedSpec,
            "Background scheduler maintenance window removed.").ConfigureAwait(false);
    }

    private async Task ClearBackgroundSchedulerMaintenanceWindowsFromUiAsync() {
        if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
            return;
        }

        var client = _client;
        if (client is null) {
            return;
        }

        await InvokeBackgroundSchedulerMutationAsync(
            static (serviceClient, cancellationToken, _) => serviceClient.SetBackgroundSchedulerMaintenanceWindowsAsync(
                "clear",
                cancellationToken: cancellationToken),
            0,
            "Background scheduler maintenance windows cleared.").ConfigureAwait(false);
    }

    private async Task SetBackgroundSchedulerThreadBlockedFromUiAsync(
        string? threadId,
        bool blocked,
        string? durationMinutesText = null,
        bool untilNextMaintenanceWindow = false,
        bool untilNextMaintenanceWindowStart = false) {
        if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
            return;
        }

        var client = _client;
        if (client is null) {
            return;
        }

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            await SetStatusAsync("Background scheduler update failed: threadId must be provided.", SessionStatusTone.Warn).ConfigureAwait(false);
            return;
        }

        int? durationSeconds = null;
        if (blocked && untilNextMaintenanceWindow && untilNextMaintenanceWindowStart) {
            await SetStatusAsync("Background scheduler update failed: choose either until the next maintenance window starts or ends.", SessionStatusTone.Warn).ConfigureAwait(false);
            return;
        }

        if (blocked && !untilNextMaintenanceWindow && !untilNextMaintenanceWindowStart && !string.IsNullOrWhiteSpace(durationMinutesText)) {
            try {
                var durationMinutes = ParseSchedulerPositiveInt(durationMinutesText, min: 1, max: 1440, nameof(durationMinutesText));
                durationSeconds = checked(durationMinutes * 60);
            } catch (Exception ex) {
                await SetStatusAsync("Background scheduler update failed: " + ex.Message, SessionStatusTone.Warn).ConfigureAwait(false);
                return;
            }
        }

        await InvokeBackgroundSchedulerMutationAsync(
            static (serviceClient, cancellationToken, state) => serviceClient.SetBackgroundSchedulerBlockedThreadsAsync(
                state.Blocked ? "add" : "remove",
                new[] { state.ThreadId },
                state.DurationSeconds,
                state.UntilNextMaintenanceWindow,
                state.UntilNextMaintenanceWindowStart,
                cancellationToken),
            (
                ThreadId: normalizedThreadId,
                Blocked: blocked,
                DurationSeconds: durationSeconds,
                UntilNextMaintenanceWindow: blocked && untilNextMaintenanceWindow,
                UntilNextMaintenanceWindowStart: blocked && untilNextMaintenanceWindowStart),
            blocked
                ? (untilNextMaintenanceWindowStart
                    ? "Background scheduler muted thread until the next maintenance window starts."
                    : untilNextMaintenanceWindow
                        ? "Background scheduler muted thread until the next maintenance window ends."
                        : durationSeconds is > 0
                            ? "Background scheduler temporarily muted thread."
                            : "Background scheduler muted thread.")
                : "Background scheduler unmuted thread.").ConfigureAwait(false);
    }

    private async Task SetBackgroundSchedulerPackBlockedFromUiAsync(
        string? packId,
        bool blocked,
        string? durationMinutesText = null,
        bool untilNextMaintenanceWindow = false,
        bool untilNextMaintenanceWindowStart = false) {
        if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
            return;
        }

        var client = _client;
        if (client is null) {
            return;
        }

        var normalizedPackId = (packId ?? string.Empty).Trim();
        if (normalizedPackId.Length == 0) {
            await SetStatusAsync("Background scheduler update failed: packId must be provided.", SessionStatusTone.Warn).ConfigureAwait(false);
            return;
        }

        int? durationSeconds = null;
        if (blocked && untilNextMaintenanceWindow && untilNextMaintenanceWindowStart) {
            await SetStatusAsync("Background scheduler update failed: choose either until the next maintenance window starts or ends.", SessionStatusTone.Warn).ConfigureAwait(false);
            return;
        }

        if (blocked && !untilNextMaintenanceWindow && !untilNextMaintenanceWindowStart && !string.IsNullOrWhiteSpace(durationMinutesText)) {
            try {
                var durationMinutes = ParseSchedulerPositiveInt(durationMinutesText, min: 1, max: 1440, nameof(durationMinutesText));
                durationSeconds = checked(durationMinutes * 60);
            } catch (Exception ex) {
                await SetStatusAsync("Background scheduler update failed: " + ex.Message, SessionStatusTone.Warn).ConfigureAwait(false);
                return;
            }
        }

        await InvokeBackgroundSchedulerMutationAsync(
            static (serviceClient, cancellationToken, state) => serviceClient.SetBackgroundSchedulerBlockedPacksAsync(
                state.Blocked ? "add" : "remove",
                new[] { state.PackId },
                state.DurationSeconds,
                state.UntilNextMaintenanceWindow,
                state.UntilNextMaintenanceWindowStart,
                cancellationToken),
            (
                PackId: normalizedPackId,
                Blocked: blocked,
                DurationSeconds: durationSeconds,
                UntilNextMaintenanceWindow: blocked && untilNextMaintenanceWindow,
                UntilNextMaintenanceWindowStart: blocked && untilNextMaintenanceWindowStart),
            blocked
                ? (untilNextMaintenanceWindowStart
                    ? "Background scheduler muted pack until the next maintenance window starts."
                    : untilNextMaintenanceWindow
                        ? "Background scheduler muted pack until the next maintenance window ends."
                        : durationSeconds is > 0
                            ? "Background scheduler temporarily muted pack."
                            : "Background scheduler muted pack.")
                : "Background scheduler unmuted pack.").ConfigureAwait(false);
    }

    private async Task ClearBackgroundSchedulerThreadBlocksFromUiAsync() {
        if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
            return;
        }

        var client = _client;
        if (client is null) {
            return;
        }

        await InvokeBackgroundSchedulerMutationAsync(
            static (serviceClient, cancellationToken, _) => serviceClient.SetBackgroundSchedulerBlockedThreadsAsync(
                "clear",
                cancellationToken: cancellationToken),
            0,
            "Background scheduler cleared muted threads.").ConfigureAwait(false);
    }

    private async Task ClearBackgroundSchedulerPackBlocksFromUiAsync() {
        if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
            return;
        }

        var client = _client;
        if (client is null) {
            return;
        }

        await InvokeBackgroundSchedulerMutationAsync(
            static (serviceClient, cancellationToken, _) => serviceClient.SetBackgroundSchedulerBlockedPacksAsync(
                "clear",
                cancellationToken: cancellationToken),
            0,
            "Background scheduler cleared muted packs.").ConfigureAwait(false);
    }

    private async Task<BackgroundSchedulerStatusMessage?> InvokeBackgroundSchedulerMutationAsync<TState>(
        Func<ChatServiceClient, CancellationToken, TState, Task<BackgroundSchedulerStatusMessage>> operation,
        TState state,
        string successPrefix) {
        var client = _client;
        if (client is null) {
            return null;
        }

        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await operation(client, cts.Token, state).ConfigureAwait(false);
            _backgroundSchedulerStatusSnapshot = null;
            ApplyBackgroundSchedulerSnapshot(result.Scheduler, scoped: false);
            await PublishOptionsStateAsync().ConfigureAwait(false);
            await SetStatusAsync(successPrefix + " " + BuildBackgroundSchedulerSummaryText(result.Scheduler)).ConfigureAwait(false);
            return result;
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                AppendSystem("Couldn't update background scheduler state: " + ex.Message);
            }
            await SetStatusAsync("Background scheduler update failed: " + ex.Message, SessionStatusTone.Warn).ConfigureAwait(false);
            return null;
        }
    }

    private static int ParseSchedulerPositiveInt(string? text, int min, int max, string paramName) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0
            || !int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed < min
            || parsed > max) {
            throw new ArgumentOutOfRangeException(paramName, $"Value must be between {min} and {max}.");
        }

        return parsed;
    }
}
