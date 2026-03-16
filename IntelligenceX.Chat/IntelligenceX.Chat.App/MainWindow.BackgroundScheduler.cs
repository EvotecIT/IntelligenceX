using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.Client;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private static void ValidateBackgroundSchedulerMaintenanceWindowScope(string? packId, string? threadId) {
        var hasPackId = !string.IsNullOrWhiteSpace(packId);
        var hasThreadId = !string.IsNullOrWhiteSpace(threadId);
        if (hasPackId && hasThreadId) {
            throw new ArgumentException("Choose either packId or threadId for a maintenance window scope, not both.", nameof(threadId));
        }
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
