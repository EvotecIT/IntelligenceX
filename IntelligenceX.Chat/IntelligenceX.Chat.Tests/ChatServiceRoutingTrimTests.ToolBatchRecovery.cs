using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Theory]
    [InlineData("fs_write_file", false)]
    [InlineData("ad_user_create", false)]
    [InlineData("system_service_restart", false)]
    [InlineData("ad_replication_summary", false)]
    [InlineData("eventlog_query", false)]
    public void IsLikelyMutatingToolName_DoesNotInferMutabilityFromToolName(string toolName, bool expected) {
        var result = IsLikelyMutatingToolNameMethod.Invoke(null, new object?[] { toolName });
        Assert.Equal(expected, Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldReplayToolCallAtLowConcurrency_AllowsTransientNonMutatingFailure() {
        var call = new ToolCall("call_001", "ad_replication_summary", null, null, new JsonObject());
        var output = new ToolOutputDto {
            CallId = "call_001",
            Output = "{\"ok\":false,\"error\":\"timed out\"}",
            Ok = false,
            ErrorCode = "tool_timeout",
            Error = "timed out after 20s",
            IsTransient = true
        };

        var result = ShouldReplayToolCallAtLowConcurrencyMethod.Invoke(null, new object?[] { call, output });
        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldReplayToolCallAtLowConcurrency_ReplaysWhenNoStructuredMutabilityHintIsAvailable() {
        var call = new ToolCall("call_002", "fs_write_file", null, null, new JsonObject());
        var output = new ToolOutputDto {
            CallId = "call_002",
            Output = "{\"ok\":false,\"error\":\"timed out\"}",
            Ok = false,
            ErrorCode = "tool_timeout",
            Error = "timed out after 20s",
            IsTransient = true
        };

        var result = ShouldReplayToolCallAtLowConcurrencyMethod.Invoke(null, new object?[] { call, output });
        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void CollectLowConcurrencyRecoveryIndexes_CapsRecoveryPassToThreeCalls() {
        var calls = new List<ToolCall>();
        var outputs = new List<ToolOutputDto>();
        for (var i = 0; i < 5; i++) {
            calls.Add(new ToolCall("call_" + i, "ad_replication_summary", null, null, new JsonObject()));
            outputs.Add(new ToolOutputDto {
                CallId = "call_" + i,
                Output = "{\"ok\":false,\"error\":\"timed out\"}",
                Ok = false,
                ErrorCode = "tool_timeout",
                Error = "timed out",
                IsTransient = true
            });
        }

        var result = CollectLowConcurrencyRecoveryIndexesMethod.Invoke(null, new object?[] { calls, outputs });
        var indexes = Assert.IsType<int[]>(result);

        Assert.Equal(3, indexes.Length);
        Assert.Equal(0, indexes[0]);
        Assert.Equal(1, indexes[1]);
        Assert.Equal(2, indexes[2]);
    }

    [Fact]
    public void ShouldRetryToolCall_RetriesTransientFailureWhenErrorCodeIsMissing() {
        var resolveRetryProfileMethod = typeof(ChatServiceSession).GetMethod(
            "ResolveRetryProfile",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(resolveRetryProfileMethod);
        var shouldRetryToolCallMethod = typeof(ChatServiceSession).GetMethod(
            "ShouldRetryToolCall",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(shouldRetryToolCallMethod);

        var profile = resolveRetryProfileMethod!.Invoke(null, new object?[] { "ad_replication_summary" });
        var output = new ToolOutputDto {
            CallId = "call_003",
            Output = "{\"ok\":false,\"error\":\"transient transport issue\"}",
            Ok = false,
            ErrorCode = null,
            Error = "transient transport issue",
            IsTransient = true
        };

        var result = shouldRetryToolCallMethod!.Invoke(null, new[] { output, profile, (object)0 });
        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldRetryToolCall_DoesNotTreatAmbiguousAuthSubstringCodeAsPermanent() {
        var resolveRetryProfileMethod = typeof(ChatServiceSession).GetMethod(
            "ResolveRetryProfile",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(resolveRetryProfileMethod);
        var shouldRetryToolCallMethod = typeof(ChatServiceSession).GetMethod(
            "ShouldRetryToolCall",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(shouldRetryToolCallMethod);

        var profile = resolveRetryProfileMethod!.Invoke(null, new object?[] { "ad_replication_summary" });
        var output = new ToolOutputDto {
            CallId = "call_004",
            Output = "{\"ok\":false,\"error_code\":\"oauth_refresh_transient\",\"error\":\"token refresh race\"}",
            Ok = false,
            ErrorCode = "oauth_refresh_transient",
            Error = "token refresh race",
            IsTransient = true
        };

        var result = shouldRetryToolCallMethod!.Invoke(null, new[] { output, profile, (object)0 });
        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void HasLikelyMutatingToolCalls_FalseWithoutStructuredHints() {
        var calls = new List<ToolCall> {
            new("call_1", "ad_replication_summary", null, null, new JsonObject()),
            new("call_2", "fs_write_file", null, null, new JsonObject())
        };

        var result = HasLikelyMutatingToolCallsMethod.Invoke(null, new object?[] { calls });
        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void BuildToolBatchRecoveringMessage_DescribesLowConcurrencyRetryPass() {
        var result = BuildToolBatchRecoveringMessageMethod.Invoke(null, new object?[] { 2, 7 });
        var text = Assert.IsType<string>(result);

        Assert.Contains("Retrying 2", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("low concurrency", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("7 total", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolBatchRecoveredMessage_ReportsRemainingFailuresWhenPresent() {
        var result = BuildToolBatchRecoveredMessageMethod.Invoke(null, new object?[] { 2, 1 });
        var text = Assert.IsType<string>(result);

        Assert.Contains("2 retried", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failure", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoundStartedMessage_ReportsRoundAndParallelMode() {
        var result = BuildToolRoundStartedMessageMethod.Invoke(null, new object?[] { 2, 6, 4, true, false });
        var text = Assert.IsType<string>(result);

        Assert.Contains("round 2/6", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4 call", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("parallel", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoundStartedMessage_ReportsMutatingParallelOverrideWhenEnabled() {
        var result = BuildToolRoundStartedMessageMethod.Invoke(null, new object?[] { 3, 6, 5, true, true });
        var text = Assert.IsType<string>(result);

        Assert.Contains("mutating override", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoundCapAppliedMessage_ReportsRequestedAndEffectiveLimits() {
        var result = BuildToolRoundCapAppliedMessageMethod.Invoke(null, new object?[] { 500, 256 });
        var text = Assert.IsType<string>(result);

        Assert.Contains("requested max tool rounds (500)", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("using 256", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoundCompletedMessage_ReportsFailureCount() {
        var result = BuildToolRoundCompletedMessageMethod.Invoke(null, new object?[] { 2, 6, 4, 2 });
        var text = Assert.IsType<string>(result);

        Assert.Contains("round 2/6", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4 call", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2 failed", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoundLimitReachedMessage_ReportsRoundBudgetAndTotals() {
        var result = BuildToolRoundLimitReachedMessageMethod.Invoke(null, new object?[] { 5, 12, 11 });
        var text = Assert.IsType<string>(result);

        Assert.Contains("max tool rounds (5)", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("12 call", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("11 output", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolHeartbeatMessage_ContainsToolAndElapsedSeconds() {
        var result = BuildToolHeartbeatMessageMethod.Invoke(null, new object?[] { "ad_replication_summary", 13 });
        var text = Assert.IsType<string>(result);

        Assert.Contains("ad_replication_summary", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("13s", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMutatingToolHintsByName_DetectsReadWriteSchemaHints() {
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("intent", new JsonObject()
                    .Add("type", "string")
                    .Add("enum", new JsonArray().Add("read_only").Add("read_write"))));
        var definitions = new List<ToolDefinition> {
            new("powershell_run", "PowerShell runtime", schema)
        };

        var result = BuildMutatingToolHintsByNameMethod.Invoke(null, new object?[] { definitions });
        var hints = Assert.IsAssignableFrom<IReadOnlyDictionary<string, bool>>(result);

        Assert.True(hints.TryGetValue("powershell_run", out var isMutating));
        Assert.True(isMutating);
    }

    [Fact]
    public void BuildMutatingToolHintsByName_DetectsReadOnlyTagHints() {
        var definitions = new List<ToolDefinition> {
            new("ad_whoami", "AD context", tags: new[] { "read_only", "inventory" })
        };

        var result = BuildMutatingToolHintsByNameMethod.Invoke(null, new object?[] { definitions });
        var hints = Assert.IsAssignableFrom<IReadOnlyDictionary<string, bool>>(result);

        Assert.True(hints.TryGetValue("ad_whoami", out var isMutating));
        Assert.False(isMutating);
    }

    [Fact]
    public void BuildMutatingToolHintsByName_PrefersWriteGovernanceContractOverReadOnlyTagHints() {
        var definitions = new List<ToolDefinition> {
            new(
                "powershell_run",
                "PowerShell runtime",
                writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue(
                    intentArgumentName: "send",
                    confirmationArgumentName: "allow_write"),
                tags: new[] { "read_only" })
        };

        var result = BuildMutatingToolHintsByNameMethod.Invoke(null, new object?[] { definitions });
        var hints = Assert.IsAssignableFrom<IReadOnlyDictionary<string, bool>>(result);

        Assert.True(hints.TryGetValue("powershell_run", out var isMutating));
        Assert.True(isMutating);
    }

    [Fact]
    public void BuildMutatingToolHintsByName_PrefersNonWriteGovernanceContractOverMutatingTagHints() {
        var definitions = new List<ToolDefinition> {
            new(
                "ad_readonly",
                "AD context",
                writeGovernance: new ToolWriteGovernanceContract { IsWriteCapable = false },
                tags: new[] { "read_write" })
        };

        var result = BuildMutatingToolHintsByNameMethod.Invoke(null, new object?[] { definitions });
        var hints = Assert.IsAssignableFrom<IReadOnlyDictionary<string, bool>>(result);

        Assert.True(hints.TryGetValue("ad_readonly", out var isMutating));
        Assert.False(isMutating);
    }

    [Fact]
    public async Task ExecuteToolWithStatusAsync_DoesNotEmitHeartbeatAfterCancellation() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registryField = typeof(ChatServiceSession).GetField("_registry", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(registryField);

        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            "slow_tool",
            static async (_, _) => {
                await Task.Delay(250);
                return """{"ok":true}""";
            }));
        registryField!.SetValue(session, registry);

        var executeToolWithStatusMethod = typeof(ChatServiceSession).GetMethod(
            "ExecuteToolWithStatusAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(executeToolWithStatusMethod);

        using var outputStream = new MemoryStream();
        using var writer = new StreamWriter(outputStream, new UTF8Encoding(false), leaveOpen: true) {
            AutoFlush = true
        };
        using var cts = new CancellationTokenSource();
        var call = new ToolCall("call_001", "slow_tool", null, null, new JsonObject());

        var taskObj = executeToolWithStatusMethod!.Invoke(
            session,
            new object?[] { writer, "req-001", "thread-001", call, 5, string.Empty, cts.Token });
        var task = Assert.IsAssignableFrom<Task<ToolOutputDto>>(taskObj);

        cts.CancelAfter(TimeSpan.FromMilliseconds(10));
        await task;

        writer.Flush();
        var statusOutput = Encoding.UTF8.GetString(outputStream.ToArray());
        var heartbeatCount = Regex.Matches(statusOutput, "tool_heartbeat", RegexOptions.CultureInvariant).Count;
        Assert.Equal(0, heartbeatCount);
    }

    [Fact]
    public async Task ExecuteToolWithStatusAsync_CancelsPromptlyForNonCooperativeTool() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registryField = typeof(ChatServiceSession).GetField("_registry", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(registryField);

        var toolGate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            "hung_tool",
            (_, _) => toolGate.Task));
        registryField!.SetValue(session, registry);

        var executeToolWithStatusMethod = typeof(ChatServiceSession).GetMethod(
            "ExecuteToolWithStatusAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(executeToolWithStatusMethod);

        using var outputStream = new MemoryStream();
        using var writer = new StreamWriter(outputStream, new UTF8Encoding(false), leaveOpen: true) {
            AutoFlush = true
        };
        using var cts = new CancellationTokenSource();
        var call = new ToolCall("call_002", "hung_tool", null, null, new JsonObject());

        var taskObj = executeToolWithStatusMethod!.Invoke(
            session,
            new object?[] { writer, "req-002", "thread-002", call, 5, string.Empty, cts.Token });
        var task = Assert.IsAssignableFrom<Task<ToolOutputDto>>(taskObj);

        cts.CancelAfter(TimeSpan.FromMilliseconds(10));
        var completion = await Task.WhenAny(task, Task.Delay(350));
        if (!ReferenceEquals(completion, task)) {
            toolGate.TrySetResult("""{"ok":true}""");
            Assert.Fail("ExecuteToolWithStatusAsync did not return promptly after cancellation.");
        }

        var output = await task;
        toolGate.TrySetResult("""{"ok":true}""");

        Assert.False(output.Ok is true);
        Assert.Equal("tool_canceled", output.ErrorCode, ignoreCase: true);

        writer.Flush();
        var statusOutput = Encoding.UTF8.GetString(outputStream.ToArray());
        Assert.Contains("tool_canceled", statusOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FinalizeToolBatchHeartbeatAsync_PreservesPrimaryFailureWhenHeartbeatAlsoFails() {
        using var cts = new CancellationTokenSource();
        var heartbeatTask = Task.FromException(new InvalidOperationException("heartbeat-failure"));
        var primaryFailure = ExceptionDispatchInfo.Capture(new ApplicationException("primary-failure"));

        var taskObj = FinalizeToolBatchHeartbeatAsyncMethod.Invoke(
            null,
            new object?[] { heartbeatTask, cts, primaryFailure });
        var task = Assert.IsAssignableFrom<Task<ExceptionDispatchInfo?>>(taskObj);

        var heartbeatFailure = await task;
        Assert.Null(heartbeatFailure);
    }

    [Fact]
    public async Task FinalizeToolBatchHeartbeatAsync_ReturnsHeartbeatFailureWhenNoPrimaryFailure() {
        using var cts = new CancellationTokenSource();
        var heartbeatTask = Task.FromException(new InvalidOperationException("heartbeat-failure"));

        var taskObj = FinalizeToolBatchHeartbeatAsyncMethod.Invoke(
            null,
            new object?[] { heartbeatTask, cts, null });
        var task = Assert.IsAssignableFrom<Task<ExceptionDispatchInfo?>>(taskObj);

        var heartbeatFailure = await task;
        var captured = Assert.IsType<ExceptionDispatchInfo>(heartbeatFailure);
        Assert.IsType<InvalidOperationException>(captured.SourceException);
        Assert.Contains("heartbeat-failure", captured.SourceException.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubTool : ITool {
        private readonly Func<JsonObject?, CancellationToken, Task<string>> _invoke;

        public StubTool(string name, Func<JsonObject?, CancellationToken, Task<string>> invoke) {
            Definition = new ToolDefinition(name, description: "stub");
            _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return _invoke(arguments, cancellationToken);
        }
    }
}
