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
        var definition = new ToolDefinition(
            name: "ad_replication_summary",
            description: "AD replication summary",
            parameters: null,
            recovery: new ToolRecoveryContract {
                IsRecoveryAware = true,
                SupportsTransientRetry = true,
                MaxRetryAttempts = 1,
                RetryableErrorCodes = new[] { "timeout", "query_failed", "transport_unavailable" }
            });
        var profile = ChatServiceSession.ResolveRetryProfileForTesting(definition);
        var output = new ToolOutputDto {
            CallId = "call_003",
            Output = "{\"ok\":false,\"error\":\"transient transport issue\"}",
            Ok = false,
            ErrorCode = null,
            Error = "transient transport issue",
            IsTransient = true
        };

        var result = ChatServiceSession.ShouldRetryToolCallForTesting(output, profile, attemptIndex: 0);
        Assert.True(result);
    }

    [Fact]
    public void ShouldRetryToolCall_DoesNotTreatAmbiguousAuthSubstringCodeAsPermanent() {
        var definition = new ToolDefinition(
            name: "ad_replication_summary",
            description: "AD replication summary",
            parameters: null,
            recovery: new ToolRecoveryContract {
                IsRecoveryAware = true,
                SupportsTransientRetry = true,
                MaxRetryAttempts = 1,
                RetryableErrorCodes = new[] { "timeout", "query_failed", "transport_unavailable" }
            });
        var profile = ChatServiceSession.ResolveRetryProfileForTesting(definition);
        var output = new ToolOutputDto {
            CallId = "call_004",
            Output = "{\"ok\":false,\"error_code\":\"oauth_refresh_transient\",\"error\":\"token refresh race\"}",
            Ok = false,
            ErrorCode = "oauth_refresh_transient",
            Error = "token refresh race",
            IsTransient = true
        };

        var result = ChatServiceSession.ShouldRetryToolCallForTesting(output, profile, attemptIndex: 0);
        Assert.True(result);
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

    [Theory]
    [InlineData("send")]
    [InlineData("execute")]
    [InlineData("apply")]
    [InlineData("disable")]
    public void BuildMutatingToolHintsByName_DetectsCanonicalMutatingSchemaArguments(string argumentName) {
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add(argumentName, new JsonObject().Add("type", "boolean")));
        var definitions = new List<ToolDefinition> {
            new("ops_write_probe", "Write probe", schema)
        };

        var result = BuildMutatingToolHintsByNameMethod.Invoke(null, new object?[] { definitions });
        var hints = Assert.IsAssignableFrom<IReadOnlyDictionary<string, bool>>(result);

        Assert.True(hints.TryGetValue("ops_write_probe", out var isMutating));
        Assert.True(isMutating);
    }

    [Fact]
    public void BuildMutatingToolHintsByName_DetectsWriteGovernanceMetadataSchemaArgumentsAsMutating() {
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add(ToolWriteGovernanceArgumentNames.OperationId, new JsonObject().Add("type", "string")));
        var definitions = new List<ToolDefinition> {
            new("ops_write_probe", "Write probe", schema)
        };

        var result = BuildMutatingToolHintsByNameMethod.Invoke(null, new object?[] { definitions });
        var hints = Assert.IsAssignableFrom<IReadOnlyDictionary<string, bool>>(result);

        Assert.True(hints.TryGetValue("ops_write_probe", out var isMutating));
        Assert.True(isMutating);
    }

    [Fact]
    public async Task ExecuteToolWithStatusAsync_DoesNotEmitHeartbeatAfterCancellation() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();

        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            "slow_tool",
            static async (_, _) => {
                await Task.Delay(250);
                return """{"ok":true}""";
            }));
        SetSessionRegistry(session, registry);

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

        var toolGate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            "hung_tool",
            (_, _) => toolGate.Task));
        SetSessionRegistry(session, registry);

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
    public async Task ExecuteToolAsync_UsesDeclaredRecoveryHelperBeforeRetry() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var mainAttempts = 0;
        var helperAttempts = 0;

        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            BuildOperationalRecoveryAwareDefinition(
                "computerx_probe",
                recoveryToolNames: new[] { "system_context" },
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject()
                        .Add("computer_name", new JsonObject().Add("type", "string")))
                    .Add("required", new JsonArray().Add("computer_name"))),
            (_, _) => {
                var attempt = Interlocked.Increment(ref mainAttempts);
                if (attempt == 1) {
                    return Task.FromResult(ToolOutputEnvelope.Error(
                        errorCode: "transport_unavailable",
                        error: "Remote transport is temporarily unavailable.",
                        isTransient: true));
                }

                return Task.FromResult("""{"ok":true,"meta":{"attempt":2}}""");
            }));
        registry.Register(new StubTool(
            BuildReadOnlyHelperDefinition(
                "system_context",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject()
                        .Add("computer_name", new JsonObject().Add("type", "string")))
                    .Add("required", new JsonArray().Add("computer_name"))),
            (arguments, _) => {
                Interlocked.Increment(ref helperAttempts);
                Assert.NotNull(arguments);
                Assert.Equal("srv-01", arguments!.GetString("computer_name"));
                return Task.FromResult("""{"ok":true,"meta":{"context":"refreshed"}}""");
            }));
        SetSessionRegistry(session, registry);

        var arguments = new JsonObject().Add("computer_name", "srv-01");
        var call = new ToolCall(
            "call_main",
            "computerx_probe",
            JsonLite.Serialize(arguments),
            arguments,
            new JsonObject());

        var output = await session.ExecuteToolAsyncForTesting("thread-001", "Check srv-01 health.", call, 5, CancellationToken.None);

        Assert.True(output.Ok is true);
        Assert.Equal(2, mainAttempts);
        Assert.Equal(1, helperAttempts);
    }

    [Fact]
    public async Task ExecuteToolAsync_SkipsDeclaredRecoveryHelperWhenRequiredArgumentsAreMissing() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var mainAttempts = 0;
        var helperAttempts = 0;

        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            BuildOperationalRecoveryAwareDefinition(
                "computerx_probe",
                recoveryToolNames: new[] { "system_context" },
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject()
                        .Add("computer_name", new JsonObject().Add("type", "string")))),
            (_, _) => {
                var attempt = Interlocked.Increment(ref mainAttempts);
                if (attempt == 1) {
                    return Task.FromResult(ToolOutputEnvelope.Error(
                        errorCode: "transport_unavailable",
                        error: "Remote transport is temporarily unavailable.",
                        isTransient: true));
                }

                return Task.FromResult("""{"ok":true}""");
            }));
        registry.Register(new StubTool(
            BuildReadOnlyHelperDefinition(
                "system_context",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject()
                        .Add("domain_name", new JsonObject().Add("type", "string")))
                    .Add("required", new JsonArray().Add("domain_name"))),
            (_, _) => {
                Interlocked.Increment(ref helperAttempts);
                return Task.FromResult("""{"ok":true}""");
            }));
        SetSessionRegistry(session, registry);

        var arguments = new JsonObject().Add("computer_name", "srv-01");
        var call = new ToolCall(
            "call_main_missing_args",
            "computerx_probe",
            JsonLite.Serialize(arguments),
            arguments,
            new JsonObject());

        var output = await session.ExecuteToolAsyncForTesting("thread-001", "Check srv-01 health.", call, 5, CancellationToken.None);

        Assert.True(output.Ok is true);
        Assert.Equal(2, mainAttempts);
        Assert.Equal(0, helperAttempts);
    }

    [Fact]
    public async Task ExecuteToolAsync_SkipsDeclaredRecoveryHelperWhenHelperIsWriteCapable() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var mainAttempts = 0;
        var helperAttempts = 0;

        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            BuildOperationalRecoveryAwareDefinition(
                "computerx_probe",
                recoveryToolNames: new[] { "system_context" }),
            (_, _) => {
                var attempt = Interlocked.Increment(ref mainAttempts);
                if (attempt == 1) {
                    return Task.FromResult(ToolOutputEnvelope.Error(
                        errorCode: "transport_unavailable",
                        error: "Remote transport is temporarily unavailable.",
                        isTransient: true));
                }

                return Task.FromResult("""{"ok":true}""");
            }));
        registry.Register(new StubTool(
            new ToolDefinition(
                "system_context",
                description: "write helper",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject()
                        .Add("send", new JsonObject().Add("type", "boolean"))
                        .Add("allow_write", new JsonObject().Add("type", "boolean")))
                    .WithWriteGovernanceMetadata(),
                writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue(
                    intentArgumentName: "send",
                    confirmationArgumentName: "allow_write"),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "test_pack",
                    Role = ToolRoutingTaxonomy.RoleEnvironmentDiscover
                }),
            (_, _) => {
                Interlocked.Increment(ref helperAttempts);
                return Task.FromResult("""{"ok":true}""");
            }));
        SetSessionRegistry(session, registry);

        var call = new ToolCall("call_main_write_helper", "computerx_probe", null, null, new JsonObject());

        var output = await session.ExecuteToolAsyncForTesting("thread-001", "Check srv-01 health.", call, 5, CancellationToken.None);

        Assert.True(output.Ok is true);
        Assert.Equal(2, mainAttempts);
        Assert.Equal(0, helperAttempts);
    }

    [Fact]
    public async Task ExecuteToolAsync_PrefersHealthyAlternateRecoveryHelperAndStopsAfterFirstSuccessfulBootstrap() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetToolRoutingStatsForTesting(new Dictionary<string, (long LastUsedUtcTicks, long LastSuccessUtcTicks)>(StringComparer.OrdinalIgnoreCase) {
            ["system_context_local"] = (DateTime.UtcNow.Ticks, DateTime.UtcNow.Ticks)
        });

        var mainAttempts = 0;
        var remoteHelperAttempts = 0;
        var localHelperAttempts = 0;

        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            BuildOperationalRecoveryAwareDefinition(
                "computerx_probe",
                recoveryToolNames: new[] { "system_context_remote", "system_context_local" },
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject()
                        .Add("computer_name", new JsonObject().Add("type", "string")))
                    .Add("required", new JsonArray().Add("computer_name"))),
            (_, _) => {
                var attempt = Interlocked.Increment(ref mainAttempts);
                if (attempt == 1) {
                    return Task.FromResult(ToolOutputEnvelope.Error(
                        errorCode: "transport_unavailable",
                        error: "Remote transport is temporarily unavailable.",
                        isTransient: true));
                }

                return Task.FromResult("""{"ok":true}""");
            }));
        registry.Register(new StubTool(
            BuildReadOnlyHelperDefinition(
                "system_context_remote",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject()
                        .Add("computer_name", new JsonObject().Add("type", "string")))
                    .Add("required", new JsonArray().Add("computer_name"))),
            (_, _) => {
                Interlocked.Increment(ref remoteHelperAttempts);
                return Task.FromResult(ToolOutputEnvelope.Error(
                    errorCode: "transport_unavailable",
                    error: "Remote helper is still unavailable.",
                    isTransient: true));
            }));
        registry.Register(new StubTool(
            BuildReadOnlyHelperDefinition(
                "system_context_local",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject()
                        .Add("computer_name", new JsonObject().Add("type", "string")))
                    .Add("required", new JsonArray().Add("computer_name"))),
            (arguments, _) => {
                Interlocked.Increment(ref localHelperAttempts);
                Assert.NotNull(arguments);
                Assert.Equal("srv-01", arguments!.GetString("computer_name"));
                return Task.FromResult("""{"ok":true,"meta":{"context":"local"}}""");
            }));
        SetSessionRegistry(session, registry);

        var arguments = new JsonObject().Add("computer_name", "srv-01");
        var call = new ToolCall(
            "call_main_health_pref",
            "computerx_probe",
            JsonLite.Serialize(arguments),
            arguments,
            new JsonObject());

        var output = await session.ExecuteToolAsyncForTesting("thread-001", "Check srv-01 health.", call, 5, CancellationToken.None);

        Assert.True(output.Ok is true);
        Assert.Equal(2, mainAttempts);
        Assert.Equal(0, remoteHelperAttempts);
        Assert.Equal(1, localHelperAttempts);
    }

    [Fact]
    public async Task ExecuteToolAsync_ReroutesToAlternateEngineWhenSchemaExplicitlySupportsIt() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var mainAttempts = 0;
        var seenEngines = new List<string>();

        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            new ToolDefinition(
                "system_inventory_probe",
                description: "alternate engine probe",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject()
                        .Add("computer_name", new JsonObject().Add("type", "string"))
                        .Add("engine", new JsonObject().Add("type", "string"))),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    SupportsTransientRetry = true,
                    MaxRetryAttempts = 1,
                    RetryableErrorCodes = new[] { "timeout", "transport_unavailable" },
                    SupportsAlternateEngines = true,
                    AlternateEngineIds = new[] { "cim", "wmi" }
                }),
            (arguments, _) => {
                Interlocked.Increment(ref mainAttempts);
                var engine = arguments?.GetString("engine") ?? string.Empty;
                seenEngines.Add(engine);
                if (!string.Equals(engine, "wmi", StringComparison.OrdinalIgnoreCase)) {
                    return Task.FromResult(ToolOutputEnvelope.Error(
                        errorCode: "transport_unavailable",
                        error: "Primary engine transport is unavailable.",
                        isTransient: true));
                }

                return Task.FromResult("""{"ok":true,"meta":{"engine":"wmi"}}""");
            }));
        SetSessionRegistry(session, registry);

        var arguments = new JsonObject()
            .Add("computer_name", "srv-01")
            .Add("engine", "cim");
        var call = new ToolCall(
            "call_main_alt_engine",
            "system_inventory_probe",
            JsonLite.Serialize(arguments),
            arguments,
            new JsonObject());

        var output = await session.ExecuteToolAsyncForTesting("thread-001", "Check srv-01 health.", call, 5, CancellationToken.None);

        Assert.True(output.Ok is true);
        Assert.Equal(2, mainAttempts);
        Assert.Equal(new[] { "cim", "wmi" }, seenEngines);
    }

    [Fact]
    public async Task ExecuteToolAsync_TriesNextAlternateEngineWhenFirstAlternateFails() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var mainAttempts = 0;
        var seenEngines = new List<string>();

        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            new ToolDefinition(
                "system_inventory_probe",
                description: "alternate engine probe",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject()
                        .Add("computer_name", new JsonObject().Add("type", "string"))
                        .Add("engine", new JsonObject()
                            .Add("type", "string")
                            .Add("enum", new JsonArray().Add("auto").Add("wmi").Add("cim")))),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    SupportsTransientRetry = true,
                    MaxRetryAttempts = 2,
                    RetryableErrorCodes = new[] { "timeout", "transport_unavailable" },
                    SupportsAlternateEngines = true,
                    AlternateEngineIds = new[] { "wmi", "cim" }
                }),
            (arguments, _) => {
                Interlocked.Increment(ref mainAttempts);
                var engine = arguments?.GetString("engine") ?? string.Empty;
                seenEngines.Add(engine);
                if (string.Equals(engine, "cim", StringComparison.OrdinalIgnoreCase)) {
                    return Task.FromResult("""{"ok":true,"meta":{"engine":"cim"}}""");
                }

                return Task.FromResult(ToolOutputEnvelope.Error(
                    errorCode: "transport_unavailable",
                    error: "Selected engine transport is unavailable.",
                    isTransient: true));
            }));
        SetSessionRegistry(session, registry);

        var arguments = new JsonObject()
            .Add("computer_name", "srv-01")
            .Add("engine", "auto");
        var call = new ToolCall(
            "call_main_alt_engine_chain",
            "system_inventory_probe",
            JsonLite.Serialize(arguments),
            arguments,
            new JsonObject());

        var output = await session.ExecuteToolAsyncForTesting("thread-001", "Check srv-01 health.", call, 5, CancellationToken.None);

        Assert.True(output.Ok is true);
        Assert.Equal(3, mainAttempts);
        Assert.Equal(new[] { "auto", "wmi", "cim" }, seenEngines);
    }

    [Fact]
    public async Task ExecuteToolAsync_PrefersPersistedHealthyAlternateEngineAfterRestart() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-alt-engine-health-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");
        var firstRunEngines = new List<string>();
        var secondRunEngines = new List<string>();

        try {
            var definition = new ToolDefinition(
                "system_inventory_probe",
                description: "alternate engine probe",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject()
                        .Add("computer_name", new JsonObject().Add("type", "string"))
                        .Add("engine", new JsonObject()
                            .Add("type", "string")
                            .Add("enum", new JsonArray().Add("auto").Add("wmi").Add("cim")))),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    SupportsTransientRetry = true,
                    MaxRetryAttempts = 2,
                    RetryableErrorCodes = new[] { "timeout", "transport_unavailable" },
                    SupportsAlternateEngines = true,
                    AlternateEngineIds = new[] { "wmi", "cim" }
                });

            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var registry1 = new ToolRegistry();
            registry1.Register(new StubTool(
                definition,
                (arguments, _) => {
                    var engine = arguments?.GetString("engine") ?? string.Empty;
                    firstRunEngines.Add(engine);
                    if (string.Equals(engine, "cim", StringComparison.OrdinalIgnoreCase)) {
                        return Task.FromResult("""{"ok":true,"meta":{"engine":"cim"}}""");
                    }

                    return Task.FromResult(ToolOutputEnvelope.Error(
                        errorCode: "transport_unavailable",
                        error: "Selected engine transport is unavailable.",
                        isTransient: true));
                }));
            SetSessionRegistry(session1, registry1);

            var firstArguments = new JsonObject()
                .Add("computer_name", "srv-01")
                .Add("engine", "auto");
            var firstCall = new ToolCall(
                "call_main_alt_engine_seed",
                "system_inventory_probe",
                JsonLite.Serialize(firstArguments),
                firstArguments,
                new JsonObject());

            var firstOutput = await session1.ExecuteToolAsyncForTesting("thread-001", "Check srv-01 health.", firstCall, 5, CancellationToken.None);

            Assert.True(firstOutput.Ok is true);
            Assert.Equal(new[] { "auto", "wmi", "cim" }, firstRunEngines);

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var registry2 = new ToolRegistry();
            registry2.Register(new StubTool(
                definition,
                (arguments, _) => {
                    var engine = arguments?.GetString("engine") ?? string.Empty;
                    secondRunEngines.Add(engine);
                    if (string.Equals(engine, "cim", StringComparison.OrdinalIgnoreCase)) {
                        return Task.FromResult("""{"ok":true,"meta":{"engine":"cim"}}""");
                    }

                    return Task.FromResult(ToolOutputEnvelope.Error(
                        errorCode: "transport_unavailable",
                        error: "Selected engine transport is unavailable.",
                        isTransient: true));
                }));
            SetSessionRegistry(session2, registry2);

            var secondArguments = new JsonObject()
                .Add("computer_name", "srv-01")
                .Add("engine", "auto");
            var secondCall = new ToolCall(
                "call_main_alt_engine_reuse",
                "system_inventory_probe",
                JsonLite.Serialize(secondArguments),
                secondArguments,
                new JsonObject());

            var secondOutput = await session2.ExecuteToolAsyncForTesting("thread-001", "Check srv-01 health.", secondCall, 5, CancellationToken.None);

            Assert.True(secondOutput.Ok is true);
            Assert.Equal(new[] { "cim" }, secondRunEngines);
        } finally {
            try {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, recursive: true);
                }
            } catch {
                // Best effort test cleanup only.
            }
        }
    }

    [Fact]
    public async Task ExecuteToolAsync_RetriesAutomaticBackendSelectionWhenPreferredHealthyBackendFailsPermanently() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberAlternateEngineSuccessForTesting("thread-001", "system_inventory_probe", "cim");
        var seenEngines = new List<string>();

        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            new ToolDefinition(
                "system_inventory_probe",
                description: "alternate engine probe",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject()
                        .Add("computer_name", new JsonObject().Add("type", "string"))
                        .Add("engine", new JsonObject()
                            .Add("type", "string")
                            .Add("enum", new JsonArray().Add("auto").Add("wmi").Add("cim")))),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    SupportsTransientRetry = true,
                    MaxRetryAttempts = 1,
                    RetryableErrorCodes = new[] { "timeout", "transport_unavailable" },
                    SupportsAlternateEngines = true,
                    AlternateEngineIds = new[] { "wmi", "cim" }
                }),
            (arguments, _) => {
                var engine = arguments?.GetString("engine") ?? string.Empty;
                seenEngines.Add(engine);
                if (string.Equals(engine, "cim", StringComparison.OrdinalIgnoreCase)) {
                    return Task.FromResult(ToolOutputEnvelope.Error(
                        errorCode: "permission_denied",
                        error: "CIM backend denied access.",
                        isTransient: false));
                }

                return Task.FromResult("""{"ok":true,"meta":{"engine":"auto"}}""");
            }));
        SetSessionRegistry(session, registry);

        var arguments = new JsonObject()
            .Add("computer_name", "srv-01")
            .Add("engine", "auto");
        var call = new ToolCall(
            "call_main_alt_engine_auto_retry",
            "system_inventory_probe",
            JsonLite.Serialize(arguments),
            arguments,
            new JsonObject());

        var output = await session.ExecuteToolAsyncForTesting("thread-001", "Check srv-01 health.", call, 5, CancellationToken.None);

        Assert.True(output.Ok is true);
        Assert.Equal(new[] { "cim", "auto" }, seenEngines);
    }

    [Fact]
    public async Task ExecuteToolAsync_DoesNotRetryAutomaticBackendSelectionAfterSuccessfulPreferredBackend() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberAlternateEngineSuccessForTesting("thread-001", "system_inventory_probe", "cim");
        var seenEngines = new List<string>();

        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            new ToolDefinition(
                "system_inventory_probe",
                description: "alternate engine probe",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject()
                        .Add("computer_name", new JsonObject().Add("type", "string"))
                        .Add("engine", new JsonObject()
                            .Add("type", "string")
                            .Add("enum", new JsonArray().Add("auto").Add("wmi").Add("cim")))),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    SupportsTransientRetry = true,
                    MaxRetryAttempts = 1,
                    RetryableErrorCodes = new[] { "timeout", "transport_unavailable" },
                    SupportsAlternateEngines = true,
                    AlternateEngineIds = new[] { "wmi", "cim" }
                }),
            (arguments, _) => {
                var engine = arguments?.GetString("engine") ?? string.Empty;
                seenEngines.Add(engine);
                return Task.FromResult("""{"ok":true,"meta":{"engine":"cim"}}""");
            }));
        SetSessionRegistry(session, registry);

        var arguments = new JsonObject()
            .Add("computer_name", "srv-01")
            .Add("engine", "auto");
        var call = new ToolCall(
            "call_main_alt_engine_auto_skip",
            "system_inventory_probe",
            JsonLite.Serialize(arguments),
            arguments,
            new JsonObject());

        var output = await session.ExecuteToolAsyncForTesting("thread-001", "Check srv-01 health.", call, 5, CancellationToken.None);

        Assert.True(output.Ok is true);
        Assert.Equal(new[] { "cim" }, seenEngines);
    }

    [Fact]
    public async Task ExecuteToolAsync_KeepsAutomaticBackendFallbackPendingAcrossPreferredBackendRetries() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberAlternateEngineSuccessForTesting("thread-001", "system_inventory_probe", "cim");
        var seenEngines = new List<string>();
        var preferredAttempts = 0;

        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            new ToolDefinition(
                "system_inventory_probe",
                description: "alternate engine probe",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject()
                        .Add("computer_name", new JsonObject().Add("type", "string"))
                        .Add("engine", new JsonObject()
                            .Add("type", "string")
                            .Add("enum", new JsonArray().Add("auto").Add("cim")))),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    SupportsTransientRetry = true,
                    MaxRetryAttempts = 2,
                    RetryableErrorCodes = new[] { "timeout", "transport_unavailable" },
                    SupportsAlternateEngines = true,
                    AlternateEngineIds = new[] { "cim" }
                }),
            (arguments, _) => {
                var engine = arguments?.GetString("engine") ?? string.Empty;
                seenEngines.Add(engine);
                if (string.Equals(engine, "cim", StringComparison.OrdinalIgnoreCase)) {
                    var attempt = Interlocked.Increment(ref preferredAttempts);
                    return Task.FromResult(attempt == 1
                        ? ToolOutputEnvelope.Error(
                            errorCode: "transport_unavailable",
                            error: "CIM transport is temporarily unavailable.",
                            isTransient: true)
                        : ToolOutputEnvelope.Error(
                            errorCode: "permission_denied",
                            error: "CIM backend denied access.",
                            isTransient: false));
                }

                return Task.FromResult("""{"ok":true,"meta":{"engine":"auto"}}""");
            }));
        SetSessionRegistry(session, registry);

        var arguments = new JsonObject()
            .Add("computer_name", "srv-01")
            .Add("engine", "auto");
        var call = new ToolCall(
            "call_main_alt_engine_auto_retry_after_retries",
            "system_inventory_probe",
            JsonLite.Serialize(arguments),
            arguments,
            new JsonObject());

        var output = await session.ExecuteToolAsyncForTesting("thread-001", "Check srv-01 health.", call, 5, CancellationToken.None);

        Assert.True(output.Ok is true);
        Assert.Equal(new[] { "cim", "cim", "auto" }, seenEngines);
    }

    [Fact]
    public async Task ExecuteToolAsync_RecordsAlternateEngineHealthFromProjectionRecoveredResult() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberAlternateEngineSuccessForTesting(
            "thread-001",
            "system_inventory_probe",
            "wmi",
            DateTime.UtcNow.AddHours(-2).Ticks);

        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            new ToolDefinition(
                "system_inventory_probe",
                description: "alternate engine probe",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject()
                        .Add("computer_name", new JsonObject().Add("type", "string"))
                        .Add("engine", new JsonObject()
                            .Add("type", "string")
                            .Add("enum", new JsonArray().Add("auto").Add("wmi").Add("cim")))
                        .Add("columns", new JsonObject()
                            .Add("type", "array"))),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    SupportsTransientRetry = true,
                    MaxRetryAttempts = 1,
                    RetryableErrorCodes = new[] { "timeout", "transport_unavailable" },
                    SupportsAlternateEngines = true,
                    AlternateEngineIds = new[] { "wmi", "cim" }
                }),
            (arguments, _) => {
                var engine = arguments?.GetString("engine") ?? string.Empty;
                var hasColumns = arguments?.TryGetValue("columns", out var columnsValue) == true && columnsValue is not null;
                if (string.Equals(engine, "cim", StringComparison.OrdinalIgnoreCase) && hasColumns) {
                    return Task.FromResult(ToolOutputEnvelope.Error(
                        errorCode: "invalid_argument",
                        error: "columns contains unsupported value 'display_name'.",
                        isTransient: false));
                }

                return Task.FromResult("""{"ok":true,"meta":{"engine":"cim"}}""");
            }));
        SetSessionRegistry(session, registry);

        var arguments = new JsonObject()
            .Add("computer_name", "srv-01")
            .Add("engine", "cim")
            .Add("columns", new JsonArray().Add("display_name"));
        var call = new ToolCall(
            "call_main_alt_engine_projection_recover",
            "system_inventory_probe",
            JsonLite.Serialize(arguments),
            arguments,
            new JsonObject());

        var output = await session.ExecuteToolAsyncForTesting("thread-001", "Check srv-01 health.", call, 5, CancellationToken.None);

        Assert.True(output.Ok is true);
        Assert.Equal(
            new[] { "cim", "wmi" },
            session.OrderAlternateEngineIdsByHealthForTesting("thread-001", "system_inventory_probe", new[] { "wmi", "cim" }));
    }

    [Fact]
    public async Task ExecuteToolWithStatusAsync_EmitsPreferredHealthyAlternateEngineStatus() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberAlternateEngineSuccessForTesting("thread-001", "system_inventory_probe", "cim");

        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            new ToolDefinition(
                "system_inventory_probe",
                description: "alternate engine probe",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject()
                        .Add("computer_name", new JsonObject().Add("type", "string"))
                        .Add("engine", new JsonObject()
                            .Add("type", "string")
                            .Add("enum", new JsonArray().Add("auto").Add("wmi").Add("cim")))),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    SupportsTransientRetry = true,
                    MaxRetryAttempts = 1,
                    RetryableErrorCodes = new[] { "timeout", "transport_unavailable" },
                    SupportsAlternateEngines = true,
                    AlternateEngineIds = new[] { "wmi", "cim" }
                }),
            (arguments, _) => {
                var engine = arguments?.GetString("engine") ?? string.Empty;
                Assert.Equal("cim", engine);
                return Task.FromResult("""{"ok":true}""");
            }));
        SetSessionRegistry(session, registry);

        var executeToolWithStatusMethod = typeof(ChatServiceSession).GetMethod(
            "ExecuteToolWithStatusAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(executeToolWithStatusMethod);

        using var outputStream = new MemoryStream();
        using var writer = new StreamWriter(outputStream, new UTF8Encoding(false), leaveOpen: true) {
            AutoFlush = true
        };
        var arguments = new JsonObject()
            .Add("computer_name", "srv-01")
            .Add("engine", "auto");
        var call = new ToolCall(
            "call_status_alt_pref",
            "system_inventory_probe",
            JsonLite.Serialize(arguments),
            arguments,
            new JsonObject());

        var taskObj = executeToolWithStatusMethod!.Invoke(
            session,
            new object?[] { writer, "req-status-1", "thread-001", call, 5, "Check srv-01 health.", CancellationToken.None });
        var task = Assert.IsAssignableFrom<Task<ToolOutputDto>>(taskObj);

        var output = await task;
        Assert.True(output.Ok is true);

        writer.Flush();
        var statusOutput = Encoding.UTF8.GetString(outputStream.ToArray());
        Assert.Contains("remembered healthy backend", statusOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cim", statusOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteToolWithStatusAsync_EmitsAlternateEngineRetryStatus() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var attempts = 0;

        var registry = new ToolRegistry();
        registry.Register(new StubTool(
            new ToolDefinition(
                "system_inventory_probe",
                description: "alternate engine probe",
                parameters: new JsonObject()
                    .Add("type", "object")
                    .Add("properties", new JsonObject()
                        .Add("computer_name", new JsonObject().Add("type", "string"))
                        .Add("engine", new JsonObject()
                            .Add("type", "string")
                            .Add("enum", new JsonArray().Add("auto").Add("wmi").Add("cim")))),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    SupportsTransientRetry = true,
                    MaxRetryAttempts = 1,
                    RetryableErrorCodes = new[] { "timeout", "transport_unavailable" },
                    SupportsAlternateEngines = true,
                    AlternateEngineIds = new[] { "wmi", "cim" }
                }),
            (arguments, _) => {
                Interlocked.Increment(ref attempts);
                var engine = arguments?.GetString("engine") ?? string.Empty;
                if (string.Equals(engine, "wmi", StringComparison.OrdinalIgnoreCase)) {
                    return Task.FromResult("""{"ok":true}""");
                }

                return Task.FromResult(ToolOutputEnvelope.Error(
                    errorCode: "transport_unavailable",
                    error: "Primary engine transport is unavailable.",
                    isTransient: true));
            }));
        SetSessionRegistry(session, registry);

        var executeToolWithStatusMethod = typeof(ChatServiceSession).GetMethod(
            "ExecuteToolWithStatusAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(executeToolWithStatusMethod);

        using var outputStream = new MemoryStream();
        using var writer = new StreamWriter(outputStream, new UTF8Encoding(false), leaveOpen: true) {
            AutoFlush = true
        };
        var arguments = new JsonObject()
            .Add("computer_name", "srv-01")
            .Add("engine", "auto");
        var call = new ToolCall(
            "call_status_alt_retry",
            "system_inventory_probe",
            JsonLite.Serialize(arguments),
            arguments,
            new JsonObject());

        var taskObj = executeToolWithStatusMethod!.Invoke(
            session,
            new object?[] { writer, "req-status-2", "thread-001", call, 5, "Check srv-01 health.", CancellationToken.None });
        var task = Assert.IsAssignableFrom<Task<ToolOutputDto>>(taskObj);

        var output = await task;
        Assert.True(output.Ok is true);
        Assert.Equal(2, attempts);

        writer.Flush();
        var statusOutput = Encoding.UTF8.GetString(outputStream.ToArray());
        Assert.Contains("Retrying with alternate backend", statusOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wmi", statusOutput, StringComparison.OrdinalIgnoreCase);
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
            Definition = BuildOperationalRecoveryAwareDefinition(name, recoveryToolNames: null);
            _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        }

        public StubTool(ToolDefinition definition, Func<JsonObject?, CancellationToken, Task<string>> invoke) {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return _invoke(arguments, cancellationToken);
        }
    }

    private static ToolDefinition BuildOperationalRecoveryAwareDefinition(
        string name,
        string[]? recoveryToolNames,
        JsonObject? parameters = null) {
        return new ToolDefinition(
            name,
            description: "stub",
            parameters: parameters,
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "test_pack",
                Role = ToolRoutingTaxonomy.RoleOperational
            },
            recovery: new ToolRecoveryContract {
                IsRecoveryAware = true,
                SupportsTransientRetry = true,
                MaxRetryAttempts = 1,
                RetryableErrorCodes = new[] { "timeout", "transport_unavailable" },
                RecoveryToolNames = recoveryToolNames ?? Array.Empty<string>()
            });
    }

    private static ToolDefinition BuildReadOnlyHelperDefinition(string name, JsonObject? parameters = null) {
        return new ToolDefinition(
            name,
            description: "helper",
            parameters: parameters,
            writeGovernance: new ToolWriteGovernanceContract { IsWriteCapable = false },
            routing: new ToolRoutingContract {
                IsRoutingAware = true,
                RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                PackId = "test_pack",
                Role = ToolRoutingTaxonomy.RoleEnvironmentDiscover
            });
    }
}
