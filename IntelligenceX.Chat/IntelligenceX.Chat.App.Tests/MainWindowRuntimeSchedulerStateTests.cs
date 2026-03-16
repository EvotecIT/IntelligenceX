using System;
using System.Reflection;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
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
}
