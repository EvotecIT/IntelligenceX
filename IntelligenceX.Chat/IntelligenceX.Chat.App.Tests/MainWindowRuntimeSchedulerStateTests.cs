using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests runtime scheduler diagnostics projected into the Chat app options payload.
/// </summary>
public sealed class MainWindowRuntimeSchedulerStateTests {
    private static readonly MethodInfo BuildCapabilitySnapshotStateMethod = typeof(MainWindow).GetMethod(
        "BuildCapabilitySnapshotState",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildCapabilitySnapshotState not found.");

    private static readonly MethodInfo BuildBackgroundSchedulerStateMethod = typeof(MainWindow).GetMethod(
        "BuildBackgroundSchedulerState",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildBackgroundSchedulerState not found.");

    private static readonly MethodInfo RestoreBackgroundSchedulerSnapshotAfterRefreshFailureMethod = typeof(MainWindow).GetMethod(
        "RestoreBackgroundSchedulerSnapshotAfterRefreshFailure",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("RestoreBackgroundSchedulerSnapshotAfterRefreshFailure not found.");

    private static readonly MethodInfo ClearBackgroundSchedulerSnapshotsMethod = typeof(MainWindow).GetMethod(
        "ClearBackgroundSchedulerSnapshots",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ClearBackgroundSchedulerSnapshots not found.");

    private static readonly MethodInfo ApplyBackgroundSchedulerSnapshotMethod = typeof(MainWindow).GetMethod(
        "ApplyBackgroundSchedulerSnapshot",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ApplyBackgroundSchedulerSnapshot not found.");

    private static readonly MethodInfo ValidateBackgroundSchedulerMaintenanceWindowScopeMethod = typeof(MainWindow).GetMethod(
        "ValidateBackgroundSchedulerMaintenanceWindowScope",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ValidateBackgroundSchedulerMaintenanceWindowScope not found.");

    private static readonly FieldInfo BackgroundSchedulerStatusSnapshotField = typeof(MainWindow).GetField(
        "_backgroundSchedulerStatusSnapshot",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_backgroundSchedulerStatusSnapshot not found.");

    private static readonly FieldInfo BackgroundSchedulerGlobalStatusSnapshotField = typeof(MainWindow).GetField(
        "_backgroundSchedulerGlobalStatusSnapshot",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_backgroundSchedulerGlobalStatusSnapshot not found.");

    /// <summary>
    /// Ensures capability snapshot diagnostics retain background scheduler details for the UI bridge.
    /// </summary>
    [Fact]
    public void BuildCapabilitySnapshotState_EmbedsBackgroundSchedulerState() {
        var snapshot = new SessionCapabilitySnapshotDto {
            RegisteredTools = 3,
            EnabledPackCount = 2,
            PluginCount = 0,
            EnabledPluginCount = 0,
            ToolingAvailable = true,
            AllowedRootCount = 1,
            BackgroundScheduler = new SessionCapabilityBackgroundSchedulerDto {
                DaemonEnabled = true,
                Paused = false,
                QueuedItemCount = 4,
                ReadyItemCount = 2,
                RunningItemCount = 1,
                TrackedThreadCount = 3,
                ActiveMaintenanceWindowSpecs = new[] { "daily@23:30/120;pack=system" },
                ActiveMaintenanceWindows = new[] {
                    new SessionCapabilityBackgroundSchedulerMaintenanceWindowDto {
                        Spec = "daily@23:30/120;pack=system",
                        Day = "daily",
                        StartTimeLocal = "23:30",
                        DurationMinutes = 120,
                        PackId = "system",
                        Scoped = true
                    }
                },
                RecentActivity = new[] {
                    new SessionCapabilityBackgroundSchedulerActivityDto {
                        Outcome = "completed",
                        ThreadId = "thread-1",
                        ItemId = "item-1",
                        ToolName = "system_disk_inventory",
                        Reason = "completed",
                        OutputCount = 2
                    }
                },
                ThreadSummaries = new[] {
                    new SessionCapabilityBackgroundSchedulerThreadSummaryDto {
                        ThreadId = "thread-1",
                        QueuedItemCount = 2,
                        ReadyItemCount = 1,
                        RunningItemCount = 0,
                        CompletedItemCount = 3,
                        PendingReadOnlyItemCount = 1,
                        PendingUnknownItemCount = 0,
                        RecentEvidenceTools = new[] { "system_disk_inventory" }
                    }
                }
            }
        };

        var state = BuildCapabilitySnapshotStateMethod.Invoke(null, new object?[] { snapshot });
        Assert.NotNull(state);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(state));
        var root = document.RootElement;
        var scheduler = root.GetProperty("backgroundScheduler");
        Assert.Equal("Scoped maintenance active for 1 window(s).", scheduler.GetProperty("statusSummary").GetString());
        Assert.Equal("system", scheduler.GetProperty("activeMaintenanceWindows")[0].GetProperty("packId").GetString());
        Assert.Equal(2, scheduler.GetProperty("readyItemCount").GetInt32());
        Assert.Equal("system_disk_inventory", scheduler.GetProperty("recentActivity")[0].GetProperty("toolName").GetString());
        Assert.Equal("thread-1", scheduler.GetProperty("threadSummaries")[0].GetProperty("threadId").GetString());
        Assert.Equal(4, scheduler.GetProperty("queuedItemCount").GetInt32());
    }

    /// <summary>
    /// Ensures paused scheduler state reports the active pause reason in UI diagnostics.
    /// </summary>
    [Fact]
    public void BuildBackgroundSchedulerState_SummarizesPausedReason() {
        var scheduler = new SessionCapabilityBackgroundSchedulerDto {
            DaemonEnabled = true,
            Paused = true,
            ManualPauseActive = true,
            PauseReason = "manual_pause:300s:maintenance"
        };

        var state = BuildBackgroundSchedulerStateMethod.Invoke(null, new object?[] { scheduler });
        Assert.NotNull(state);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(state));
        Assert.Equal("Paused: manual_pause:300s:maintenance", document.RootElement.GetProperty("statusSummary").GetString());
    }

    /// <summary>
    /// Ensures global scheduled maintenance pauses are summarized distinctly from scoped maintenance activity.
    /// </summary>
    [Fact]
    public void BuildBackgroundSchedulerState_SummarizesGlobalScheduledMaintenancePause() {
        var scheduler = new SessionCapabilityBackgroundSchedulerDto {
            DaemonEnabled = true,
            Paused = true,
            ScheduledPauseActive = true,
            ActiveMaintenanceWindowSpecs = new[] { "daily@23:30/120" },
            ActiveMaintenanceWindows = new[] {
                new SessionCapabilityBackgroundSchedulerMaintenanceWindowDto {
                    Spec = "daily@23:30/120",
                    Day = "daily",
                    StartTimeLocal = "23:30",
                    DurationMinutes = 120,
                    Scoped = false
                }
            }
        };

        var state = BuildBackgroundSchedulerStateMethod.Invoke(null, new object?[] { scheduler });
        Assert.NotNull(state);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(state));
        Assert.Equal("Global maintenance active for 1 window(s).", document.RootElement.GetProperty("statusSummary").GetString());
    }

    /// <summary>
    /// Ensures scheduler state published for the UI keeps tracked-thread counts plus ready/running id samples
    /// even when not every tracked thread has a detailed thread summary entry.
    /// </summary>
    [Fact]
    public void BuildCapabilitySnapshotState_EmbedsReadyThreadIdsWhenThreadSummariesAreSampled() {
        var snapshot = new SessionCapabilitySnapshotDto {
            RegisteredTools = 0,
            EnabledPackCount = 0,
            PluginCount = 0,
            EnabledPluginCount = 0,
            ToolingAvailable = true,
            AllowedRootCount = 0,
            BackgroundScheduler = new SessionCapabilityBackgroundSchedulerDto {
                DaemonEnabled = true,
                TrackedThreadCount = 8,
                ReadyItemCount = 3,
                ReadyThreadIds = new[] { "thread-missing" },
                ThreadSummaries = new[] {
                    new SessionCapabilityBackgroundSchedulerThreadSummaryDto {
                        ThreadId = "thread-visible",
                        QueuedItemCount = 0,
                        ReadyItemCount = 1,
                        RunningItemCount = 0,
                        CompletedItemCount = 0,
                        PendingReadOnlyItemCount = 1,
                        PendingUnknownItemCount = 0
                    }
                }
            }
        };

        var state = BuildCapabilitySnapshotStateMethod.Invoke(null, new object?[] { snapshot });
        Assert.NotNull(state);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(state));
        var scheduler = document.RootElement.GetProperty("backgroundScheduler");
        Assert.Equal(8, scheduler.GetProperty("trackedThreadCount").GetInt32());
        Assert.Equal("thread-missing", scheduler.GetProperty("readyThreadIds")[0].GetString());
        Assert.Equal("thread-visible", scheduler.GetProperty("threadSummaries")[0].GetProperty("threadId").GetString());
    }

    /// <summary>
    /// Ensures app refresh keeps larger thread-id samples for sidebar fallback without inflating
    /// the caller's requested thread summary cap.
    /// </summary>
    [Fact]
    public void ResolveBackgroundSchedulerThreadSummaryLimit_RespectsRequestedCap() {
        var threadIdSampleLimit = MainWindow.ResolveBackgroundSchedulerThreadIdSampleLimit(includeThreadSummaries: true);
        var threadSummaryLimit = MainWindow.ResolveBackgroundSchedulerThreadSummaryLimit(maxThreadSummaries: 8);

        Assert.Equal(ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems, threadIdSampleLimit);
        Assert.Equal(8, threadSummaryLimit);
    }

    /// <summary>
    /// Ensures app refresh clamps invalid negative thread summary caps to the request contract floor.
    /// </summary>
    [Fact]
    public void ResolveBackgroundSchedulerThreadSummaryLimit_ClampsNegativeValuesToZero() {
        var threadSummaryLimit = MainWindow.ResolveBackgroundSchedulerThreadSummaryLimit(maxThreadSummaries: -5);

        Assert.Equal(0, threadSummaryLimit);
    }

    /// <summary>
    /// Ensures the app rejects ambiguous maintenance-window scope selections before building or sending a request.
    /// </summary>
    [Fact]
    public void ValidateBackgroundSchedulerMaintenanceWindowScope_RejectsPackAndThreadTogether() {
        var ex = Assert.Throws<TargetInvocationException>(() =>
            ValidateBackgroundSchedulerMaintenanceWindowScopeMethod.Invoke(null, new object?[] { "system", "thread-42" }));

        var argument = Assert.IsType<ArgumentException>(ex.InnerException);
        Assert.Contains("either packId or threadId", argument.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures scoped maintenance-window validation still allows pack-only and thread-only requests.
    /// </summary>
    [Fact]
    public void ValidateBackgroundSchedulerMaintenanceWindowScope_AllowsSingleScopeTarget() {
        ValidateBackgroundSchedulerMaintenanceWindowScopeMethod.Invoke(null, new object?[] { "system", null });
        ValidateBackgroundSchedulerMaintenanceWindowScopeMethod.Invoke(null, new object?[] { null, "thread-42" });
        ValidateBackgroundSchedulerMaintenanceWindowScopeMethod.Invoke(null, new object?[] { null, null });
    }

    /// <summary>
    /// Ensures a scoped scheduler refresh failure restores the preserved global snapshot
    /// instead of blanking the active scheduler diagnostics view.
    /// </summary>
    [Fact]
    public void RestoreBackgroundSchedulerSnapshotAfterRefreshFailure_ScopedRefreshFallsBackToGlobalSnapshot() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var globalSnapshot = new SessionCapabilityBackgroundSchedulerDto {
            DaemonEnabled = true,
            QueuedItemCount = 5
        };
        var scopedSnapshot = new SessionCapabilityBackgroundSchedulerDto {
            ScopeThreadId = "thread-scoped",
            QueuedItemCount = 1
        };

        BackgroundSchedulerGlobalStatusSnapshotField.SetValue(window, globalSnapshot);
        BackgroundSchedulerStatusSnapshotField.SetValue(window, scopedSnapshot);

        RestoreBackgroundSchedulerSnapshotAfterRefreshFailureMethod.Invoke(window, new object?[] { true });

        var restored = Assert.IsType<SessionCapabilityBackgroundSchedulerDto>(BackgroundSchedulerStatusSnapshotField.GetValue(window));
        var preservedGlobal = Assert.IsType<SessionCapabilityBackgroundSchedulerDto>(BackgroundSchedulerGlobalStatusSnapshotField.GetValue(window));
        Assert.Same(globalSnapshot, restored);
        Assert.Same(globalSnapshot, preservedGlobal);
    }

    /// <summary>
    /// Ensures disconnect/cache-clear cleanup blanks both scoped and global scheduler snapshots
    /// before the next options payload is published to the web shell.
    /// </summary>
    [Fact]
    public void ClearBackgroundSchedulerSnapshots_ClearsScopedAndGlobalSnapshots() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        BackgroundSchedulerStatusSnapshotField.SetValue(window, new SessionCapabilityBackgroundSchedulerDto {
            ScopeThreadId = "thread-scoped",
            QueuedItemCount = 1
        });
        BackgroundSchedulerGlobalStatusSnapshotField.SetValue(window, new SessionCapabilityBackgroundSchedulerDto {
            QueuedItemCount = 4
        });

        ClearBackgroundSchedulerSnapshotsMethod.Invoke(window, Array.Empty<object?>());

        Assert.Null(BackgroundSchedulerStatusSnapshotField.GetValue(window));
        Assert.Null(BackgroundSchedulerGlobalStatusSnapshotField.GetValue(window));
    }

    /// <summary>
    /// Ensures successful unscoped scheduler updates replace any previously cached scoped snapshot
    /// so the published runtime scheduler state reflects the new global result immediately.
    /// </summary>
    [Fact]
    public void ApplyBackgroundSchedulerSnapshot_UnscopedUpdateReplacesScopedSnapshot() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var refreshedGlobalSnapshot = new SessionCapabilityBackgroundSchedulerDto {
            QueuedItemCount = 7
        };

        BackgroundSchedulerStatusSnapshotField.SetValue(window, new SessionCapabilityBackgroundSchedulerDto {
            ScopeThreadId = "thread-scoped",
            QueuedItemCount = 1
        });
        BackgroundSchedulerGlobalStatusSnapshotField.SetValue(window, new SessionCapabilityBackgroundSchedulerDto {
            QueuedItemCount = 3
        });

        ApplyBackgroundSchedulerSnapshotMethod.Invoke(window, new object?[] { refreshedGlobalSnapshot, false });

        var effective = Assert.IsType<SessionCapabilityBackgroundSchedulerDto>(BackgroundSchedulerStatusSnapshotField.GetValue(window));
        var global = Assert.IsType<SessionCapabilityBackgroundSchedulerDto>(BackgroundSchedulerGlobalStatusSnapshotField.GetValue(window));
        Assert.Same(refreshedGlobalSnapshot, effective);
        Assert.Same(refreshedGlobalSnapshot, global);
    }
}
