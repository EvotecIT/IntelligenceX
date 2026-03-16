using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ToolHealthContractTests {
    [Fact]
    public void CheckToolHealthRequest_DeserializesViaPolymorphicContract() {
        const string json = """
            {
              "type":"check_tool_health",
              "requestId":"req_1",
              "toolTimeoutSeconds":4,
              "sourceKinds":["closedSource","openSource"],
              "packIds":["ad","testimox"]
            }
            """;

        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest);
        var request = Assert.IsType<CheckToolHealthRequest>(parsed);

        Assert.Equal("req_1", request.RequestId);
        Assert.Equal(4, request.ToolTimeoutSeconds);
        Assert.NotNull(request.SourceKinds);
        Assert.Equal(2, request.SourceKinds!.Length);
        Assert.Contains(ToolPackSourceKind.ClosedSource, request.SourceKinds);
        Assert.Contains(ToolPackSourceKind.OpenSource, request.SourceKinds);
        Assert.NotNull(request.PackIds);
        Assert.Equal("ad", request.PackIds![0]);
    }

    [Fact]
    public void GetBackgroundSchedulerStatusRequest_DeserializesViaPolymorphicContract() {
        const string json = """
            {
              "type":"get_background_scheduler_status",
              "requestId":"req_scheduler_1",
              "threadId":"thread-42",
              "includeRecentActivity":false,
              "maxReadyThreadIds":1,
              "maxRunningThreadIds":0,
              "maxRecentActivity":2,
              "maxThreadSummaries":3
            }
            """;

        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest);
        var request = Assert.IsType<GetBackgroundSchedulerStatusRequest>(parsed);

        Assert.Equal("req_scheduler_1", request.RequestId);
        Assert.Equal("thread-42", request.ThreadId);
        Assert.False(request.IncludeRecentActivity);
        Assert.Equal(1, request.MaxReadyThreadIds);
        Assert.Equal(0, request.MaxRunningThreadIds);
        Assert.Equal(2, request.MaxRecentActivity);
        Assert.Equal(3, request.MaxThreadSummaries);
    }

    [Fact]
    public void SetBackgroundSchedulerStateRequest_DeserializesViaPolymorphicContract() {
        const string json = """
            {
              "type":"set_background_scheduler_state",
              "requestId":"req_scheduler_control_1",
              "paused":true,
              "pauseSeconds":120,
              "reason":"maintenance window"
            }
            """;

        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest);
        var request = Assert.IsType<SetBackgroundSchedulerStateRequest>(parsed);

        Assert.Equal("req_scheduler_control_1", request.RequestId);
        Assert.True(request.Paused);
        Assert.Equal(120, request.PauseSeconds);
        Assert.Equal("maintenance window", request.Reason);
    }

    [Fact]
    public void SetBackgroundSchedulerMaintenanceWindowsRequest_DeserializesViaPolymorphicContract() {
        const string json = """
            {
              "type":"set_background_scheduler_maintenance_windows",
              "requestId":"req_scheduler_windows_1",
              "operation":"replace",
              "windows":["mon@02:00/60","daily@23:30/120"]
            }
            """;

        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest);
        var request = Assert.IsType<SetBackgroundSchedulerMaintenanceWindowsRequest>(parsed);

        Assert.Equal("req_scheduler_windows_1", request.RequestId);
        Assert.Equal("replace", request.Operation);
        Assert.Equal(new[] { "mon@02:00/60", "daily@23:30/120" }, request.Windows);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedThreadsRequest_DeserializesViaPolymorphicContract() {
        const string json = """
            {
              "type":"set_background_scheduler_blocked_threads",
              "requestId":"req_scheduler_threads_1",
              "operation":"add",
              "threadIds":["thread-a"],
              "untilNextMaintenanceWindow":true
            }
            """;

        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest);
        var request = Assert.IsType<SetBackgroundSchedulerBlockedThreadsRequest>(parsed);

        Assert.Equal("req_scheduler_threads_1", request.RequestId);
        Assert.Equal("add", request.Operation);
        Assert.Equal(new[] { "thread-a" }, request.ThreadIds);
        Assert.Null(request.DurationSeconds);
        Assert.True(request.UntilNextMaintenanceWindow);
        Assert.False(request.UntilNextMaintenanceWindowStart);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedPacksRequest_DeserializesViaPolymorphicContract() {
        const string json = """
            {
              "type":"set_background_scheduler_blocked_packs",
              "requestId":"req_scheduler_packs_1",
              "operation":"add",
              "packIds":["system","active_directory"],
              "durationSeconds":120
            }
            """;

        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest);
        var request = Assert.IsType<SetBackgroundSchedulerBlockedPacksRequest>(parsed);

        Assert.Equal("req_scheduler_packs_1", request.RequestId);
        Assert.Equal("add", request.Operation);
        Assert.Equal(new[] { "system", "active_directory" }, request.PackIds);
        Assert.Equal(120, request.DurationSeconds);
        Assert.False(request.UntilNextMaintenanceWindow);
        Assert.False(request.UntilNextMaintenanceWindowStart);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedPacksRequest_UntilNextMaintenanceWindow_DeserializesViaPolymorphicContract() {
        const string json = """
            {
              "type":"set_background_scheduler_blocked_packs",
              "requestId":"req_scheduler_packs_2",
              "operation":"add",
              "packIds":["system"],
              "untilNextMaintenanceWindow":true
            }
            """;

        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest);
        var request = Assert.IsType<SetBackgroundSchedulerBlockedPacksRequest>(parsed);

        Assert.Equal("req_scheduler_packs_2", request.RequestId);
        Assert.Equal("add", request.Operation);
        Assert.Equal(new[] { "system" }, request.PackIds);
        Assert.Null(request.DurationSeconds);
        Assert.True(request.UntilNextMaintenanceWindow);
        Assert.False(request.UntilNextMaintenanceWindowStart);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedThreadsRequest_UntilNextMaintenanceWindowStart_DeserializesViaPolymorphicContract() {
        const string json = """
            {
              "type":"set_background_scheduler_blocked_threads",
              "requestId":"req_scheduler_threads_2",
              "operation":"add",
              "threadIds":["thread-a"],
              "untilNextMaintenanceWindowStart":true
            }
            """;

        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest);
        var request = Assert.IsType<SetBackgroundSchedulerBlockedThreadsRequest>(parsed);

        Assert.Equal("req_scheduler_threads_2", request.RequestId);
        Assert.Equal("add", request.Operation);
        Assert.Equal(new[] { "thread-a" }, request.ThreadIds);
        Assert.Null(request.DurationSeconds);
        Assert.False(request.UntilNextMaintenanceWindow);
        Assert.True(request.UntilNextMaintenanceWindowStart);
    }

    [Fact]
    public void ToolHealthMessage_RoundTripsProbeMetadata() {
        var message = new ToolHealthMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = "req_2",
            OkCount = 2,
            FailedCount = 1,
            Probes = new[] {
                new ToolHealthProbeDto {
                    ToolName = "eventlog_pack_info",
                    PackId = "eventlog",
                    PackName = "Event Log",
                    SourceKind = ToolPackSourceKind.Builtin,
                    Ok = true,
                    DurationMs = 12
                },
                new ToolHealthProbeDto {
                    ToolName = "ad_pack_info",
                    PackId = "ad",
                    PackName = "ADPlayground",
                    SourceKind = ToolPackSourceKind.ClosedSource,
                    Ok = false,
                    ErrorCode = "provider_unavailable",
                    Error = "Domain controller not reachable.",
                    DurationMs = 53
                }
            }
        };

        var json = JsonSerializer.Serialize<ChatServiceMessage>(message, ChatServiceJsonContext.Default.ChatServiceMessage);
        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceMessage);
        var typed = Assert.IsType<ToolHealthMessage>(parsed);

        Assert.Equal(2, typed.OkCount);
        Assert.Equal(1, typed.FailedCount);
        Assert.Equal(2, typed.Probes.Length);
        Assert.Equal("ad_pack_info", typed.Probes[1].ToolName);
        Assert.Equal(ToolPackSourceKind.ClosedSource, typed.Probes[1].SourceKind);
        Assert.Equal("provider_unavailable", typed.Probes[1].ErrorCode);
    }

    [Fact]
    public void BackgroundSchedulerStatusMessage_RoundTripsSchedulerSummary() {
        var message = new BackgroundSchedulerStatusMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = "req_scheduler_2",
            Scheduler = new SessionCapabilityBackgroundSchedulerDto {
                ScopeThreadId = "thread-ready",
                SupportsPersistentQueue = true,
                SupportsReadOnlyAutoReplay = true,
                SupportsCrossThreadScheduling = true,
                DaemonEnabled = true,
                AutoPauseEnabled = true,
                ManualPauseActive = true,
                ScheduledPauseActive = false,
                FailureThreshold = 3,
                FailurePauseSeconds = 180,
                MaintenanceWindowSpecs = new[] { "mon@02:00/60" },
                MaintenanceWindows = new[] {
                    new SessionCapabilityBackgroundSchedulerMaintenanceWindowDto {
                        Spec = "mon@02:00/60",
                        Day = "mon",
                        StartTimeLocal = "02:00",
                        DurationMinutes = 60,
                        Scoped = false
                    }
                },
                ActiveMaintenanceWindowSpecs = new[] { "mon@02:00/60;pack=system" },
                ActiveMaintenanceWindows = new[] {
                    new SessionCapabilityBackgroundSchedulerMaintenanceWindowDto {
                        Spec = "mon@02:00/60;pack=system",
                        Day = "mon",
                        StartTimeLocal = "02:00",
                        DurationMinutes = 60,
                        PackId = "system",
                        Scoped = true
                    }
                },
                AllowedPackIds = new[] { "system", "eventlog" },
                BlockedPackIds = new[] { "active_directory" },
                BlockedPackSuppressions = new[] {
                    new SessionCapabilityBackgroundSchedulerSuppressionDto {
                        Id = "active_directory",
                        Mode = "persistent_runtime",
                        Temporary = false
                    }
                },
                AllowedThreadIds = new[] { "thread-ready", "thread-running" },
                BlockedThreadIds = new[] { "thread-blocked" },
                BlockedThreadSuppressions = new[] {
                    new SessionCapabilityBackgroundSchedulerSuppressionDto {
                        Id = "thread-blocked",
                        Mode = "temporary_runtime",
                        Temporary = true,
                        ExpiresUtcTicks = DateTime.UtcNow.AddMinutes(15).Ticks
                    }
                },
                Paused = true,
                PausedUntilUtcTicks = DateTime.UtcNow.AddMinutes(3).Ticks,
                PauseReason = "consecutive_failure_threshold_reached:requeued_after_tool_failure:system_info",
                TrackedThreadCount = 2,
                ReadyThreadCount = 1,
                RunningThreadCount = 1,
                QueuedItemCount = 2,
                ReadyItemCount = 1,
                RunningItemCount = 1,
                CompletedItemCount = 4,
                PendingReadOnlyItemCount = 1,
                PendingUnknownItemCount = 0,
                LastSchedulerTickUtcTicks = DateTime.UtcNow.Ticks,
                LastOutcomeUtcTicks = DateTime.UtcNow.Ticks,
                LastSuccessUtcTicks = DateTime.UtcNow.AddMinutes(-2).Ticks,
                LastFailureUtcTicks = DateTime.UtcNow.AddMinutes(-1).Ticks,
                CompletedExecutionCount = 5,
                RequeuedExecutionCount = 2,
                ReleasedExecutionCount = 1,
                ConsecutiveFailureCount = 3,
                LastOutcome = "requeued_after_tool_failure",
                ReadyThreadIds = new[] { "thread-ready" },
                RunningThreadIds = new[] { "thread-running" },
                RecentActivity = new[] {
                    new SessionCapabilityBackgroundSchedulerActivityDto {
                        RecordedUtcTicks = DateTime.UtcNow.Ticks,
                        Outcome = "requeued_after_tool_failure",
                        ThreadId = "thread-ready",
                        ItemId = "tool_handoff:system_info:machine_name:srv1",
                        ToolName = "system_info",
                        Reason = "background_scheduler_requeued_after_tool_failure",
                        OutputCount = 1,
                        FailureDetail = "remote_probe_failed"
                    }
                },
                ThreadSummaries = new[] {
                    new SessionCapabilityBackgroundSchedulerThreadSummaryDto {
                        ThreadId = "thread-ready",
                        QueuedItemCount = 0,
                        ReadyItemCount = 1,
                        RunningItemCount = 0,
                        CompletedItemCount = 0,
                        PendingReadOnlyItemCount = 1,
                        PendingUnknownItemCount = 0,
                        RecentEvidenceTools = new[] { "remote_disk_inventory" }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize<ChatServiceMessage>(message, ChatServiceJsonContext.Default.ChatServiceMessage);
        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceMessage);
        var typed = Assert.IsType<BackgroundSchedulerStatusMessage>(parsed);

        Assert.True(typed.Scheduler.DaemonEnabled);
        Assert.True(typed.Scheduler.AutoPauseEnabled);
        Assert.True(typed.Scheduler.ManualPauseActive);
        Assert.False(typed.Scheduler.ScheduledPauseActive);
        Assert.Equal("thread-ready", typed.Scheduler.ScopeThreadId);
        Assert.Equal(new[] { "mon@02:00/60" }, typed.Scheduler.MaintenanceWindowSpecs);
        Assert.Equal("02:00", Assert.Single(typed.Scheduler.MaintenanceWindows).StartTimeLocal);
        Assert.Equal(new[] { "mon@02:00/60;pack=system" }, typed.Scheduler.ActiveMaintenanceWindowSpecs);
        Assert.Equal("system", Assert.Single(typed.Scheduler.ActiveMaintenanceWindows).PackId);
        Assert.Equal(new[] { "system", "eventlog" }, typed.Scheduler.AllowedPackIds);
        Assert.Equal(new[] { "active_directory" }, typed.Scheduler.BlockedPackIds);
        Assert.Equal("persistent_runtime", Assert.Single(typed.Scheduler.BlockedPackSuppressions).Mode);
        Assert.Equal(new[] { "thread-ready", "thread-running" }, typed.Scheduler.AllowedThreadIds);
        Assert.Equal(new[] { "thread-blocked" }, typed.Scheduler.BlockedThreadIds);
        Assert.True(Assert.Single(typed.Scheduler.BlockedThreadSuppressions).Temporary);
        Assert.True(typed.Scheduler.Paused);
        Assert.Equal("requeued_after_tool_failure", typed.Scheduler.LastOutcome);
        Assert.Equal("thread-ready", Assert.Single(typed.Scheduler.ReadyThreadIds));
        Assert.Equal("remote_probe_failed", Assert.Single(typed.Scheduler.RecentActivity).FailureDetail);
        Assert.Equal("thread-ready", Assert.Single(typed.Scheduler.ThreadSummaries).ThreadId);
    }

    [Fact]
    public void SessionCapabilityBackgroundSchedulerDto_SourceGenRoundTripsSuppressionArrays() {
        var scheduler = new SessionCapabilityBackgroundSchedulerDto {
            BlockedPackIds = new[] { "system" },
            BlockedPackSuppressions = new[] {
                new SessionCapabilityBackgroundSchedulerSuppressionDto {
                    Id = "system",
                    Mode = "persistent_runtime",
                    Temporary = false
                }
            },
            BlockedThreadIds = new[] { "thread-a" },
            BlockedThreadSuppressions = new[] {
                new SessionCapabilityBackgroundSchedulerSuppressionDto {
                    Id = "thread-a",
                    Mode = "temporary_runtime",
                    Temporary = true,
                    ExpiresUtcTicks = DateTime.UtcNow.AddMinutes(5).Ticks
                }
            }
        };

        var json = JsonSerializer.Serialize(scheduler, ChatServiceJsonContext.Default.SessionCapabilityBackgroundSchedulerDto);
        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.SessionCapabilityBackgroundSchedulerDto);

        Assert.NotNull(parsed);
        Assert.Equal("system", Assert.Single(parsed.BlockedPackSuppressions).Id);
        Assert.Equal("persistent_runtime", Assert.Single(parsed.BlockedPackSuppressions).Mode);
        Assert.Equal("thread-a", Assert.Single(parsed.BlockedThreadSuppressions).Id);
        Assert.True(Assert.Single(parsed.BlockedThreadSuppressions).Temporary);
    }
}
