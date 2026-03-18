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
    public void GetBackgroundSchedulerStatusRequest_RejectsNegativeSampleLimits() {
        const string json = """
            {
              "type":"get_background_scheduler_status",
              "requestId":"req_scheduler_limits_negative",
              "maxReadyThreadIds":-1
            }
            """;

        var ex = Assert.ThrowsAny<ArgumentOutOfRangeException>(() => JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest));
        Assert.Contains("MaxReadyThreadIds", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetBackgroundSchedulerStatusRequest_RejectsOversizedSampleLimits() {
        var oversized = ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems + 25;
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new GetBackgroundSchedulerStatusRequest {
            RequestId = "req_scheduler_limits_max",
            MaxReadyThreadIds = oversized,
            MaxRunningThreadIds = oversized,
            MaxRecentActivity = oversized,
            MaxThreadSummaries = oversized
        });

        Assert.Contains("MaxReadyThreadIds", ex.Message, StringComparison.Ordinal);
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
    public void SetBackgroundSchedulerStateRequest_RejectsNonPositivePauseSeconds() {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new SetBackgroundSchedulerStateRequest {
            RequestId = "req_scheduler_control_invalid",
            Paused = true,
            PauseSeconds = 0
        });

        Assert.Contains("PauseSeconds", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerStateRequest_RejectsNonPositivePauseSecondsDuringPolymorphicDeserialization() {
        const string json = """
            {
              "type":"set_background_scheduler_state",
              "requestId":"req_scheduler_control_invalid_wire",
              "paused":true,
              "pauseSeconds":0
            }
            """;

        var ex = Assert.ThrowsAny<ArgumentOutOfRangeException>(() => JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest));
        Assert.Contains("PauseSeconds", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerStateRequest_TrimsReason() {
        var request = new SetBackgroundSchedulerStateRequest {
            RequestId = "req_scheduler_control_reason",
            Paused = true,
            Reason = "  maintenance window  "
        };

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
    public void SetBackgroundSchedulerMaintenanceWindowsRequest_NormalizesOperation() {
        var request = new SetBackgroundSchedulerMaintenanceWindowsRequest(
            "req_scheduler_windows_normalized",
            "  RePlace  ",
            new[] { "mon@02:00/60" });

        Assert.Equal("replace", request.Operation);
    }

    [Fact]
    public void SetBackgroundSchedulerMaintenanceWindowsRequest_RejectsInvalidOperation() {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerMaintenanceWindowsRequest(
            "req_scheduler_windows_invalid",
            " mutate ",
            new[] { "mon@02:00/60" }));

        Assert.Contains("Operation must be one of", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerMaintenanceWindowsRequest_RejectsDuplicateWindowsAfterNormalization() {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerMaintenanceWindowsRequest(
            "req_scheduler_windows_duplicate_targets",
            "add",
            new[] { "mon@02:00/60", "  MON@02:00/60  " }));

        Assert.Contains("windows", ex.ParamName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duplicate targets", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("replace")]
    public void SetBackgroundSchedulerMaintenanceWindowsRequest_RequiresWindowsForTargetedOperations(string operation) {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerMaintenanceWindowsRequest(
            "req_scheduler_windows_missing_targets",
            operation));

        Assert.Contains("Windows must be provided", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("reset")]
    public void SetBackgroundSchedulerMaintenanceWindowsRequest_RejectsWindowsForUntargetedOperations(string operation) {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerMaintenanceWindowsRequest(
            "req_scheduler_windows_unexpected_targets",
            operation,
            new[] { "mon@02:00/60" }));

        Assert.Contains("Windows must be omitted", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerMaintenanceWindowsRequest_RejectsMissingWindowsDuringPolymorphicDeserialization() {
        const string json = """
            {
              "type":"set_background_scheduler_maintenance_windows",
              "requestId":"req_scheduler_windows_missing_targets_wire",
              "operation":"add"
            }
            """;

        var ex = Assert.ThrowsAny<ArgumentException>(() => JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest));
        Assert.Contains("Windows must be provided", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerMaintenanceWindowsRequest_RejectsInvalidOperationDuringPolymorphicDeserialization() {
        const string json = """
            {
              "type":"set_background_scheduler_maintenance_windows",
              "requestId":"req_scheduler_windows_invalid_wire",
              "operation":"mutate",
              "windows":["mon@02:00/60"]
            }
            """;

        var ex = Assert.ThrowsAny<ArgumentException>(() => JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest));
        Assert.Contains("Operation must be one of", ex.Message, StringComparison.Ordinal);
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
    public void SetBackgroundSchedulerBlockedThreadsRequest_RejectsConflictingFlags() {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedThreadsRequest(
            "req_scheduler_threads_invalid",
            "add",
            new[] { "thread-a" },
            durationSeconds: 60,
            untilNextMaintenanceWindow: true,
            untilNextMaintenanceWindowStart: true));

        Assert.Contains("cannot both be true", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedThreadsRequest_NormalizesOperation() {
        var request = new SetBackgroundSchedulerBlockedThreadsRequest(
            "req_scheduler_threads_normalized",
            "  Add  ",
            new[] { "thread-a" });

        Assert.Equal("add", request.Operation);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedThreadsRequest_RejectsInvalidOperation() {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedThreadsRequest(
            "req_scheduler_threads_invalid_operation",
            "  mutate  ",
            new[] { "thread-a" }));

        Assert.Contains("Operation must be one of", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedThreadsRequest_RejectsDuplicateThreadIdsAfterNormalization() {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedThreadsRequest(
            "req_scheduler_threads_duplicate_targets",
            "add",
            new[] { "thread-a", "  thread-a  ", "THREAD-A" }));

        Assert.Contains("threadIds", ex.ParamName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duplicate targets", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedThreadsRequest_RejectsDuplicateThreadIdsDuringPolymorphicDeserialization() {
        const string json = """
            {
              "type":"set_background_scheduler_blocked_threads",
              "requestId":"req_scheduler_threads_duplicate_targets_wire",
              "operation":"add",
              "threadIds":["thread-a","  thread-a  ","THREAD-A"]
            }
            """;

        var ex = Assert.ThrowsAny<ArgumentException>(() => JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest));
        Assert.Contains("threadIds", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duplicate targets", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("replace")]
    public void SetBackgroundSchedulerBlockedThreadsRequest_RequiresThreadIdsForTargetedOperations(string operation) {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedThreadsRequest(
            "req_scheduler_threads_missing_targets",
            operation));

        Assert.Contains("ThreadIds must be provided", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("reset")]
    public void SetBackgroundSchedulerBlockedThreadsRequest_RejectsThreadIdsForUntargetedOperations(string operation) {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedThreadsRequest(
            "req_scheduler_threads_unexpected_targets",
            operation,
            new[] { "thread-a" }));

        Assert.Contains("ThreadIds must be omitted", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("replace")]
    [InlineData("clear")]
    [InlineData("reset")]
    public void SetBackgroundSchedulerBlockedThreadsRequest_RejectsTemporaryControlsForNonAddOperations(string operation) {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedThreadsRequest(
            "req_scheduler_threads_unexpected_temporary",
            operation,
            operation is "remove" or "replace" ? new[] { "thread-a" } : null,
            durationSeconds: 60));

        Assert.Contains("only supported for add operations", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedThreadsRequest_RejectsDurationCombinedWithMaintenanceWindowFlag() {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedThreadsRequest(
            "req_scheduler_threads_conflicting_temporary",
            "add",
            new[] { "thread-a" },
            durationSeconds: 60,
            untilNextMaintenanceWindow: true));

        Assert.Contains("cannot be combined", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedThreadsRequest_RejectsTemporaryControlsForRemoveDuringPolymorphicDeserialization() {
        const string json = """
            {
              "type":"set_background_scheduler_blocked_threads",
              "requestId":"req_scheduler_threads_unexpected_temporary_wire",
              "operation":"remove",
              "threadIds":["thread-a"],
              "durationSeconds":60
            }
            """;

        var ex = Assert.ThrowsAny<ArgumentException>(() => JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest));
        Assert.Contains("only supported for add operations", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedThreadsRequest_RejectsConflictingFlagsDuringPolymorphicDeserialization() {
        const string json = """
            {
              "type":"set_background_scheduler_blocked_threads",
              "requestId":"req_scheduler_threads_wire",
              "operation":"add",
              "threadIds":["thread-a"],
              "untilNextMaintenanceWindow":true,
              "untilNextMaintenanceWindowStart":true
            }
            """;

        var ex = Assert.ThrowsAny<ArgumentException>(() => JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest));
        Assert.Contains("cannot both be true", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedThreadsRequest_RejectsInvalidOperationDuringPolymorphicDeserialization() {
        const string json = """
            {
              "type":"set_background_scheduler_blocked_threads",
              "requestId":"req_scheduler_threads_invalid_operation_wire",
              "operation":"mutate",
              "threadIds":["thread-a"]
            }
            """;

        var ex = Assert.ThrowsAny<ArgumentException>(() => JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest));
        Assert.Contains("Operation must be one of", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedThreadsRequest_RejectsNonPositiveDuration() {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new SetBackgroundSchedulerBlockedThreadsRequest(
            "req_scheduler_threads_invalid_duration",
            "add",
            new[] { "thread-a" },
            durationSeconds: 0));

        Assert.Contains("DurationSeconds", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedThreadsRequest_RejectsNonPositiveDurationDuringPolymorphicDeserialization() {
        const string json = """
            {
              "type":"set_background_scheduler_blocked_threads",
              "requestId":"req_scheduler_threads_invalid_duration_wire",
              "operation":"add",
              "threadIds":["thread-a"],
              "durationSeconds":0
            }
            """;

        var ex = Assert.ThrowsAny<ArgumentOutOfRangeException>(() => JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest));
        Assert.Contains("DurationSeconds", ex.Message, StringComparison.Ordinal);
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
    public void SetBackgroundSchedulerBlockedPacksRequest_RejectsConflictingFlags() {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedPacksRequest(
            "req_scheduler_packs_invalid",
            "add",
            new[] { "system" },
            durationSeconds: 60,
            untilNextMaintenanceWindow: true,
            untilNextMaintenanceWindowStart: true));

        Assert.Contains("cannot both be true", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedPacksRequest_NormalizesOperation() {
        var request = new SetBackgroundSchedulerBlockedPacksRequest(
            "req_scheduler_packs_normalized",
            "  Remove  ",
            new[] { "system" });

        Assert.Equal("remove", request.Operation);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedPacksRequest_RejectsInvalidOperation() {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedPacksRequest(
            "req_scheduler_packs_invalid_operation",
            "  mutate  ",
            new[] { "system" }));

        Assert.Contains("Operation must be one of", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedPacksRequest_RejectsDuplicatePackIdsAfterNormalization() {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedPacksRequest(
            "req_scheduler_packs_duplicate_targets",
            "add",
            new[] { "system", "  system  ", "SYSTEM" }));

        Assert.Contains("packIds", ex.ParamName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duplicate targets", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedPacksRequest_RejectsDuplicatePackIdsDuringPolymorphicDeserialization() {
        const string json = """
            {
              "type":"set_background_scheduler_blocked_packs",
              "requestId":"req_scheduler_packs_duplicate_targets_wire",
              "operation":"add",
              "packIds":["system","  system  ","SYSTEM"]
            }
            """;

        var ex = Assert.ThrowsAny<ArgumentException>(() => JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest));
        Assert.Contains("packIds", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duplicate targets", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("replace")]
    public void SetBackgroundSchedulerBlockedPacksRequest_RequiresPackIdsForTargetedOperations(string operation) {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedPacksRequest(
            "req_scheduler_packs_missing_targets",
            operation));

        Assert.Contains("PackIds must be provided", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("reset")]
    public void SetBackgroundSchedulerBlockedPacksRequest_RejectsPackIdsForUntargetedOperations(string operation) {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedPacksRequest(
            "req_scheduler_packs_unexpected_targets",
            operation,
            new[] { "system" }));

        Assert.Contains("PackIds must be omitted", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("replace")]
    [InlineData("clear")]
    [InlineData("reset")]
    public void SetBackgroundSchedulerBlockedPacksRequest_RejectsTemporaryControlsForNonAddOperations(string operation) {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedPacksRequest(
            "req_scheduler_packs_unexpected_temporary",
            operation,
            operation is "remove" or "replace" ? new[] { "system" } : null,
            durationSeconds: 60));

        Assert.Contains("only supported for add operations", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedPacksRequest_RejectsDurationCombinedWithMaintenanceWindowFlag() {
        var ex = Assert.Throws<ArgumentException>(() => new SetBackgroundSchedulerBlockedPacksRequest(
            "req_scheduler_packs_conflicting_temporary",
            "add",
            new[] { "system" },
            durationSeconds: 60,
            untilNextMaintenanceWindowStart: true));

        Assert.Contains("cannot be combined", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedPacksRequest_RejectsTargetsForClearDuringPolymorphicDeserialization() {
        const string json = """
            {
              "type":"set_background_scheduler_blocked_packs",
              "requestId":"req_scheduler_packs_unexpected_targets_wire",
              "operation":"clear",
              "packIds":["system"]
            }
            """;

        var ex = Assert.ThrowsAny<ArgumentException>(() => JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest));
        Assert.Contains("PackIds must be omitted", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedPacksRequest_RejectsConflictingFlagsDuringPolymorphicDeserialization() {
        const string json = """
            {
              "type":"set_background_scheduler_blocked_packs",
              "requestId":"req_scheduler_packs_wire",
              "operation":"add",
              "packIds":["system"],
              "untilNextMaintenanceWindow":true,
              "untilNextMaintenanceWindowStart":true
            }
            """;

        var ex = Assert.ThrowsAny<ArgumentException>(() => JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest));
        Assert.Contains("cannot both be true", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedPacksRequest_RejectsInvalidOperationDuringPolymorphicDeserialization() {
        const string json = """
            {
              "type":"set_background_scheduler_blocked_packs",
              "requestId":"req_scheduler_packs_invalid_operation_wire",
              "operation":"mutate",
              "packIds":["system"]
            }
            """;

        var ex = Assert.ThrowsAny<ArgumentException>(() => JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest));
        Assert.Contains("Operation must be one of", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedPacksRequest_RejectsNonPositiveDuration() {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new SetBackgroundSchedulerBlockedPacksRequest(
            "req_scheduler_packs_invalid_duration",
            "add",
            new[] { "system" },
            durationSeconds: 0));

        Assert.Contains("DurationSeconds", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetBackgroundSchedulerBlockedPacksRequest_RejectsNonPositiveDurationDuringPolymorphicDeserialization() {
        const string json = """
            {
              "type":"set_background_scheduler_blocked_packs",
              "requestId":"req_scheduler_packs_invalid_duration_wire",
              "operation":"add",
              "packIds":["system"],
              "durationSeconds":0
            }
            """;

        var ex = Assert.ThrowsAny<ArgumentOutOfRangeException>(() => JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceRequest));
        Assert.Contains("DurationSeconds", ex.Message, StringComparison.Ordinal);
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
                DependencyBlockedThreadCount = 1,
                QueuedItemCount = 2,
                DependencyBlockedItemCount = 1,
                DependencyHelperToolNames = new[] { "eventlog_channels_list" },
                DependencyRecoveryReason = "background_prerequisite_auth_context_required",
                DependencyNextAction = "request_runtime_auth_context",
                DependencyRetryCooldownHelperToolNames = Array.Empty<string>(),
                DependencyAuthenticationHelperToolNames = new[] { "eventlog_channels_list" },
                DependencyAuthenticationArgumentNames = new[] { "profile_id" },
                DependencySetupHelperToolNames = Array.Empty<string>(),
                ReadyItemCount = 1,
                RunningItemCount = 1,
                CompletedItemCount = 4,
                PendingReadOnlyItemCount = 1,
                PendingUnknownItemCount = 0,
                LastSchedulerTickUtcTicks = DateTime.UtcNow.Ticks,
                LastOutcomeUtcTicks = DateTime.UtcNow.Ticks,
                LastSuccessUtcTicks = DateTime.UtcNow.AddMinutes(-2).Ticks,
                LastFailureUtcTicks = DateTime.UtcNow.AddMinutes(-1).Ticks,
                AdaptiveIdleActive = true,
                LastAdaptiveIdleUtcTicks = DateTime.UtcNow.AddSeconds(-8).Ticks,
                LastAdaptiveIdleDelaySeconds = 12,
                LastAdaptiveIdleReason = "background_scheduler_fresh_reuse_window:eventlog_probe_reuse_window:thread=thread-ready:remaining=48s",
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
                        DependencyBlockedItemCount = 0,
                        ReadyItemCount = 1,
                        RunningItemCount = 0,
                        CompletedItemCount = 0,
                        PendingReadOnlyItemCount = 1,
                        PendingUnknownItemCount = 0,
                        RecentEvidenceTools = new[] { "remote_disk_inventory" },
                        DependencyHelperToolNames = Array.Empty<string>(),
                        ReusedHelperItemCount = 1,
                        ReusedHelperToolNames = new[] { "eventlog_channels_list" },
                        ReusedHelperPolicyNames = new[] { "eventlog_probe_reuse_window" },
                        ReusedHelperFreshestAgeSeconds = 42,
                        ReusedHelperOldestAgeSeconds = 42,
                        ReusedHelperFreshestTtlSeconds = 300,
                        ReusedHelperOldestTtlSeconds = 300,
                        DependencyRecoveryReason = "background_prerequisite_auth_context_required",
                        DependencyNextAction = "request_runtime_auth_context",
                        ContinuationHint = new SessionCapabilityBackgroundSchedulerContinuationHintDto {
                            ThreadId = "thread-ready",
                            NextAction = "request_runtime_auth_context",
                            RecoveryReason = "background_prerequisite_auth_context_required",
                            HelperToolNames = new[] { "eventlog_channels_list" },
                            InputArgumentNames = new[] { "profile_id" },
                            SuggestedRequests = new[] {
                                new SessionCapabilityBackgroundSchedulerContinuationRequestDto {
                                    RequestKind = "list_profiles",
                                    Purpose = "discover_runtime_profiles"
                                },
                                new SessionCapabilityBackgroundSchedulerContinuationRequestDto {
                                    RequestKind = "set_profile",
                                    Purpose = "apply_runtime_auth_context",
                                    RequiredArgumentNames = new[] { "profileName" },
                                    SatisfiesInputArgumentNames = new[] { "profile_id" },
                                    SuggestedArguments = new[] {
                                        new SessionCapabilityBackgroundSchedulerContinuationRequestArgumentDto {
                                            Name = "newThread",
                                            Value = "false",
                                            ValueKind = "boolean"
                                        }
                                    }
                                }
                            },
                            StatusSummary = "Waiting on runtime auth context: profile_id."
                        },
                        DependencyRetryCooldownHelperToolNames = Array.Empty<string>(),
                        DependencyAuthenticationHelperToolNames = new[] { "eventlog_channels_list" },
                        DependencyAuthenticationArgumentNames = new[] { "profile_id" },
                        DependencySetupHelperToolNames = Array.Empty<string>()
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
        Assert.True(typed.Scheduler.AdaptiveIdleActive);
        Assert.Equal(12, typed.Scheduler.LastAdaptiveIdleDelaySeconds);
        Assert.Contains("eventlog_probe_reuse_window", typed.Scheduler.LastAdaptiveIdleReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("thread-ready", Assert.Single(typed.Scheduler.ReadyThreadIds));
        Assert.Equal("remote_probe_failed", Assert.Single(typed.Scheduler.RecentActivity).FailureDetail);
        Assert.Equal("thread-ready", Assert.Single(typed.Scheduler.ThreadSummaries).ThreadId);
        Assert.Equal(1, Assert.Single(typed.Scheduler.ThreadSummaries).ReusedHelperItemCount);
        Assert.Equal("eventlog_channels_list", Assert.Single(Assert.Single(typed.Scheduler.ThreadSummaries).ReusedHelperToolNames));
        Assert.Equal("eventlog_probe_reuse_window", Assert.Single(Assert.Single(typed.Scheduler.ThreadSummaries).ReusedHelperPolicyNames));
        Assert.Equal(42, Assert.Single(typed.Scheduler.ThreadSummaries).ReusedHelperFreshestAgeSeconds);
        Assert.Equal(42, Assert.Single(typed.Scheduler.ThreadSummaries).ReusedHelperOldestAgeSeconds);
        Assert.Equal(300, Assert.Single(typed.Scheduler.ThreadSummaries).ReusedHelperFreshestTtlSeconds);
        Assert.Equal(300, Assert.Single(typed.Scheduler.ThreadSummaries).ReusedHelperOldestTtlSeconds);
        Assert.Equal("request_runtime_auth_context", typed.Scheduler.DependencyNextAction);
        Assert.Equal("eventlog_channels_list", Assert.Single(typed.Scheduler.DependencyHelperToolNames));
        Assert.Equal("background_prerequisite_auth_context_required", Assert.Single(typed.Scheduler.ThreadSummaries).DependencyRecoveryReason);
        Assert.Equal("request_runtime_auth_context", Assert.Single(typed.Scheduler.ThreadSummaries).DependencyNextAction);
        Assert.Equal("request_runtime_auth_context", Assert.Single(typed.Scheduler.ThreadSummaries).ContinuationHint!.NextAction);
        Assert.Equal("profile_id", Assert.Single(Assert.Single(typed.Scheduler.ThreadSummaries).ContinuationHint!.InputArgumentNames));
        Assert.Equal(2, Assert.Single(typed.Scheduler.ThreadSummaries).ContinuationHint!.SuggestedRequests.Length);
        Assert.Equal(
            "set_profile",
            Assert.Single(
                Assert.Single(typed.Scheduler.ThreadSummaries).ContinuationHint!.SuggestedRequests,
                static request => string.Equals(request.Purpose, "apply_runtime_auth_context", StringComparison.Ordinal)).RequestKind);
        Assert.Equal("profile_id", Assert.Single(typed.Scheduler.ThreadSummaries).DependencyAuthenticationArgumentNames[0]);
        Assert.Equal(1, typed.Scheduler.DependencyBlockedThreadCount);
        Assert.Equal(1, typed.Scheduler.DependencyBlockedItemCount);
    }

    [Fact]
    public void SessionCapabilityBackgroundSchedulerDto_SourceGenRoundTripsSuppressionArrays() {
        var scheduler = new SessionCapabilityBackgroundSchedulerDto {
            DependencyBlockedThreadCount = 1,
            DependencyBlockedItemCount = 2,
            DependencyHelperToolNames = new[] { "eventlog_channels_list" },
            DependencyRecoveryReason = "background_prerequisite_auth_context_required",
            DependencyNextAction = "request_runtime_auth_context",
            DependencyRetryCooldownHelperToolNames = Array.Empty<string>(),
            DependencyAuthenticationHelperToolNames = new[] { "eventlog_channels_list" },
            DependencyAuthenticationArgumentNames = new[] { "profile_id" },
            DependencySetupHelperToolNames = Array.Empty<string>(),
            AdaptiveIdleActive = true,
            LastAdaptiveIdleUtcTicks = DateTime.UtcNow.AddSeconds(-6).Ticks,
            LastAdaptiveIdleDelaySeconds = 15,
            LastAdaptiveIdleReason = "background_scheduler_fresh_reuse_window:eventlog_probe_reuse_window:thread=thread-a:remaining=54s",
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
        Assert.Equal(1, parsed.DependencyBlockedThreadCount);
        Assert.Equal(2, parsed.DependencyBlockedItemCount);
        Assert.Equal("request_runtime_auth_context", parsed.DependencyNextAction);
        Assert.Equal("eventlog_channels_list", Assert.Single(parsed.DependencyHelperToolNames));
        Assert.Equal("profile_id", Assert.Single(parsed.DependencyAuthenticationArgumentNames));
        Assert.True(parsed.AdaptiveIdleActive);
        Assert.Equal(15, parsed.LastAdaptiveIdleDelaySeconds);
        Assert.Contains("eventlog_probe_reuse_window", parsed.LastAdaptiveIdleReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("system", Assert.Single(parsed.BlockedPackSuppressions).Id);
        Assert.Equal("persistent_runtime", Assert.Single(parsed.BlockedPackSuppressions).Mode);
        Assert.Equal("thread-a", Assert.Single(parsed.BlockedThreadSuppressions).Id);
        Assert.True(Assert.Single(parsed.BlockedThreadSuppressions).Temporary);
    }

    [Fact]
    public void SessionCapabilitySnapshotDto_SourceGenRoundTripsBackgroundScheduler() {
        var snapshot = new SessionCapabilitySnapshotDto {
            RegisteredTools = 4,
            EnabledPackCount = 2,
            PluginCount = 0,
            EnabledPluginCount = 0,
            ToolingAvailable = true,
            AllowedRootCount = 1,
            DangerousToolsEnabled = true,
            DangerousPackIds = new[] { "active_directory_lifecycle" },
            RepresentativeExamples = new[] { "inspect host posture before expanding into remote follow-up" },
            CrossPackTargetPackDisplayNames = new[] { "Event Log", "System" },
            Autonomy = new SessionCapabilityAutonomySummaryDto {
                RemoteCapableToolCount = 2,
                TargetScopedToolCount = 3,
                RemoteHostTargetingToolCount = 1,
                WriteCapableToolCount = 1,
                AuthenticationRequiredToolCount = 1,
                ProbeCapableToolCount = 1,
                RemoteCapablePackIds = new[] { "eventlog", "system" },
                TargetScopedPackIds = new[] { "active_directory", "eventlog" },
                RemoteHostTargetingPackIds = new[] { "eventlog" },
                WriteCapablePackIds = new[] { "active_directory_lifecycle" },
                AuthenticationRequiredPackIds = new[] { "eventlog" },
                ProbeCapablePackIds = new[] { "eventlog" }
            },
            BackgroundScheduler = new SessionCapabilityBackgroundSchedulerDto {
                DaemonEnabled = true,
                DependencyBlockedThreadCount = 1,
                DependencyBlockedItemCount = 2,
                DependencyHelperToolNames = new[] { "eventlog_channels_list" },
                DependencyRecoveryReason = "background_prerequisite_auth_context_required",
                DependencyNextAction = "request_runtime_auth_context",
                DependencyAuthenticationHelperToolNames = new[] { "eventlog_channels_list" },
                DependencyAuthenticationArgumentNames = new[] { "profile_id" },
                AdaptiveIdleActive = true,
                LastAdaptiveIdleUtcTicks = DateTime.UtcNow.AddSeconds(-4).Ticks,
                LastAdaptiveIdleDelaySeconds = 10,
                LastAdaptiveIdleReason = "background_scheduler_fresh_reuse_window:eventlog_probe_reuse_window:thread=thread-a:remaining=36s",
                QueuedItemCount = 3,
                ReadyThreadIds = new[] { "thread-a" },
                BlockedThreadSuppressions = new[] {
                    new SessionCapabilityBackgroundSchedulerSuppressionDto {
                        Id = "thread-a",
                        Mode = "temporary_runtime",
                        Temporary = true,
                        ExpiresUtcTicks = DateTime.UtcNow.AddMinutes(10).Ticks
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(snapshot, ChatServiceJsonContext.Default.SessionCapabilitySnapshotDto);
        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.SessionCapabilitySnapshotDto);

        Assert.NotNull(parsed);
        Assert.NotNull(parsed.BackgroundScheduler);
        Assert.True(parsed.DangerousToolsEnabled);
        Assert.Equal(new[] { "active_directory_lifecycle" }, parsed.DangerousPackIds);
        Assert.Equal("inspect host posture before expanding into remote follow-up", Assert.Single(parsed.RepresentativeExamples));
        Assert.Equal(new[] { "Event Log", "System" }, parsed.CrossPackTargetPackDisplayNames);
        Assert.NotNull(parsed.Autonomy);
        Assert.Equal(3, parsed.Autonomy!.TargetScopedToolCount);
        Assert.Equal(1, parsed.Autonomy.RemoteHostTargetingToolCount);
        Assert.Equal(1, parsed.Autonomy.WriteCapableToolCount);
        Assert.Equal(1, parsed.Autonomy.AuthenticationRequiredToolCount);
        Assert.Equal(1, parsed.Autonomy.ProbeCapableToolCount);
        Assert.True(parsed.BackgroundScheduler.DaemonEnabled);
        Assert.Equal(1, parsed.BackgroundScheduler.DependencyBlockedThreadCount);
        Assert.Equal(2, parsed.BackgroundScheduler.DependencyBlockedItemCount);
        Assert.Equal("request_runtime_auth_context", parsed.BackgroundScheduler.DependencyNextAction);
        Assert.Equal("profile_id", Assert.Single(parsed.BackgroundScheduler.DependencyAuthenticationArgumentNames));
        Assert.True(parsed.BackgroundScheduler.AdaptiveIdleActive);
        Assert.Equal(10, parsed.BackgroundScheduler.LastAdaptiveIdleDelaySeconds);
        Assert.Contains("eventlog_probe_reuse_window", parsed.BackgroundScheduler.LastAdaptiveIdleReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("thread-a", Assert.Single(parsed.BackgroundScheduler.ReadyThreadIds));
        Assert.Equal("temporary_runtime", Assert.Single(parsed.BackgroundScheduler.BlockedThreadSuppressions).Mode);
    }
}
