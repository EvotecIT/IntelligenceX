using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Guards wire-contract tokens and shared bounds used across host/service/app.
/// </summary>
public sealed class ChatContractsProtocolStabilityTests {
    [Fact]
    public void ChatStatusCodes_ExposeStableWireTokens() {
        Assert.Equal("accepted", ChatStatusCodes.Accepted);
        Assert.Equal("thinking", ChatStatusCodes.Thinking);
        Assert.Equal("context_ready", ChatStatusCodes.ContextReady);
        Assert.Equal("turn_queued", ChatStatusCodes.TurnQueued);
        Assert.Equal("execution_lane_waiting", ChatStatusCodes.ExecutionLaneWaiting);
        Assert.Equal("execution_lane_acquired", ChatStatusCodes.ExecutionLaneAcquired);
        Assert.Equal("model_selected", ChatStatusCodes.ModelSelected);
        Assert.Equal("routing", ChatStatusCodes.Routing);
        Assert.Equal("routing_meta", ChatStatusCodes.RoutingMeta);
        Assert.Equal("routing_tool", ChatStatusCodes.RoutingTool);
        Assert.Equal("tool_call", ChatStatusCodes.ToolCall);
        Assert.Equal("tool_running", ChatStatusCodes.ToolRunning);
        Assert.Equal("tool_heartbeat", ChatStatusCodes.ToolHeartbeat);
        Assert.Equal("tool_completed", ChatStatusCodes.ToolCompleted);
        Assert.Equal("tool_canceled", ChatStatusCodes.ToolCanceled);
        Assert.Equal("tool_recovered", ChatStatusCodes.ToolRecovered);
        Assert.Equal("tool_parallel_mode", ChatStatusCodes.ToolParallelMode);
        Assert.Equal("tool_parallel_forced", ChatStatusCodes.ToolParallelForced);
        Assert.Equal("tool_parallel_safety_off", ChatStatusCodes.ToolParallelSafetyOff);
        Assert.Equal("tool_batch_started", ChatStatusCodes.ToolBatchStarted);
        Assert.Equal("tool_batch_progress", ChatStatusCodes.ToolBatchProgress);
        Assert.Equal("tool_batch_heartbeat", ChatStatusCodes.ToolBatchHeartbeat);
        Assert.Equal("tool_batch_recovering", ChatStatusCodes.ToolBatchRecovering);
        Assert.Equal("tool_batch_recovered", ChatStatusCodes.ToolBatchRecovered);
        Assert.Equal("tool_batch_completed", ChatStatusCodes.ToolBatchCompleted);
        Assert.Equal("tool_round_started", ChatStatusCodes.ToolRoundStarted);
        Assert.Equal("tool_round_completed", ChatStatusCodes.ToolRoundCompleted);
        Assert.Equal("tool_replay_compacted", ChatStatusCodes.ToolReplayCompacted);
        Assert.Equal("tool_round_limit_reached", ChatStatusCodes.ToolRoundLimitReached);
        Assert.Equal("tool_round_cap_applied", ChatStatusCodes.ToolRoundCapApplied);
        Assert.Equal("review_passes_clamped", ChatStatusCodes.ReviewPassesClamped);
        Assert.Equal("model_heartbeat_clamped", ChatStatusCodes.ModelHeartbeatClamped);
        Assert.Equal("phase_plan", ChatStatusCodes.PhasePlan);
        Assert.Equal("phase_execute", ChatStatusCodes.PhaseExecute);
        Assert.Equal("phase_review", ChatStatusCodes.PhaseReview);
        Assert.Equal("phase_heartbeat", ChatStatusCodes.PhaseHeartbeat);
        Assert.Equal("background_work_queued", ChatStatusCodes.BackgroundWorkQueued);
        Assert.Equal("background_work_ready", ChatStatusCodes.BackgroundWorkReady);
        Assert.Equal("background_work_running", ChatStatusCodes.BackgroundWorkRunning);
        Assert.Equal("background_work_completed", ChatStatusCodes.BackgroundWorkCompleted);
        Assert.Equal("done", ChatStatusCodes.Done);
        Assert.Equal("error", ChatStatusCodes.Error);
        Assert.Equal("timeout", ChatStatusCodes.Timeout);
        Assert.Equal("no_result_watchdog_triggered", ChatStatusCodes.NoResultWatchdogTriggered);
    }

    [Fact]
    public void ChatStatusCodes_AreUniqueAndExplicitlyEnumerated() {
        var values = typeof(ChatStatusCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(44, values.Length);
        Assert.Equal(values.Length, values.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ChatRequestOptionLimits_ExposeStableSafetyBounds() {
        Assert.Equal(1, ChatRequestOptionLimits.MinToolRounds);
        Assert.Equal(256, ChatRequestOptionLimits.MaxToolRounds);
        Assert.Equal(24, ChatRequestOptionLimits.DefaultToolRounds);
        Assert.Equal(0, ChatRequestOptionLimits.MinCandidateTools);
        Assert.Equal(256, ChatRequestOptionLimits.MaxCandidateTools);
        Assert.Equal(512, ChatRequestOptionLimits.MaxToolSelectors);
        Assert.Equal(128, ChatRequestOptionLimits.MaxToolSelectorLength);
        Assert.Equal(0, ChatRequestOptionLimits.MinTimeoutSeconds);
        Assert.Equal(3600, ChatRequestOptionLimits.MaxTimeoutSeconds);
        Assert.Equal(1, ChatRequestOptionLimits.MinPositiveTimeoutSeconds);
        Assert.Equal(1, ChatRequestOptionLimits.DefaultReviewPasses);
        Assert.Equal(3, ChatRequestOptionLimits.MaxReviewPasses);
        Assert.Equal(8, ChatRequestOptionLimits.DefaultModelHeartbeatSeconds);
        Assert.Equal(60, ChatRequestOptionLimits.MaxModelHeartbeatSeconds);
    }

    [Fact]
    public void ToolCallDto_JsonContract_UsesArgumentsJsonAndOmitsLegacyInputOnWrite() {
        var expectedArguments = """{"domain_name":"contoso.local"}""";
        var dto = new ToolCallDto {
            CallId = "call_001",
            Name = "ad_scope_discovery",
            ArgumentsJson = expectedArguments
        };

        var json = JsonSerializer.Serialize(dto, ChatServiceJsonContext.Default.ToolCallDto);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("call_001", root.GetProperty("callId").GetString());
        Assert.Equal("ad_scope_discovery", root.GetProperty("name").GetString());
        Assert.Equal(expectedArguments, root.GetProperty("argumentsJson").GetString());
        Assert.DoesNotContain("\"input\":", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolCallDto_JsonContract_AcceptsLegacyInputAliasOnRead() {
        var json = """
            {
              "callId": "call_legacy",
              "name": "ad_scope_discovery",
              "input": "{\"domain_name\":\"contoso.local\"}"
            }
            """;

        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ToolCallDto);
        var dto = Assert.IsType<ToolCallDto>(parsed);
        Assert.Equal("call_legacy", dto.CallId);
        Assert.Equal("ad_scope_discovery", dto.Name);
        Assert.Equal("""{"domain_name":"contoso.local"}""", dto.ArgumentsJson);
    }

    [Fact]
    public void ToolCallDto_LegacyInputInitializer_MapsToArgumentsJson() {
#pragma warning disable CS0618
        var dto = new ToolCallDto {
            CallId = "call_init",
            Name = "ad_scope_discovery",
            Input = """{"domain_name":"contoso.local"}"""
        };
#pragma warning restore CS0618

        Assert.Equal("""{"domain_name":"contoso.local"}""", dto.ArgumentsJson);
    }

    [Fact]
    public void ToolCallDto_CanonicalArgumentsJson_WinsOverLegacyInputRegardlessOfOrder() {
        const string canonical = """{"domain_name":"canonical.local"}""";
        const string legacy = """{"domain_name":"legacy.local"}""";
        const string callId = "call_precedence";
        const string toolName = "ad_scope_discovery";

#pragma warning disable CS0618
        var initializerCanonicalFirst = new ToolCallDto {
            CallId = callId,
            Name = toolName,
            ArgumentsJson = canonical,
            Input = legacy
        };
        var initializerInputFirst = new ToolCallDto {
            CallId = callId,
            Name = toolName,
            Input = legacy,
            ArgumentsJson = canonical
        };
#pragma warning restore CS0618

        var jsonCanonicalFirst = """
            {
              "callId": "call_precedence_json_a",
              "name": "ad_scope_discovery",
              "argumentsJson": "{\"domain_name\":\"canonical.local\"}",
              "input": "{\"domain_name\":\"legacy.local\"}"
            }
            """;
        var jsonInputFirst = """
            {
              "callId": "call_precedence_json_b",
              "name": "ad_scope_discovery",
              "input": "{\"domain_name\":\"legacy.local\"}",
              "argumentsJson": "{\"domain_name\":\"canonical.local\"}"
            }
            """;
        var fromJsonCanonicalFirst = Assert.IsType<ToolCallDto>(
            JsonSerializer.Deserialize(jsonCanonicalFirst, ChatServiceJsonContext.Default.ToolCallDto));
        var fromJsonInputFirst = Assert.IsType<ToolCallDto>(
            JsonSerializer.Deserialize(jsonInputFirst, ChatServiceJsonContext.Default.ToolCallDto));

        Assert.Equal(canonical, initializerCanonicalFirst.ArgumentsJson);
        Assert.Equal(canonical, initializerInputFirst.ArgumentsJson);
        Assert.Equal(canonical, fromJsonCanonicalFirst.ArgumentsJson);
        Assert.Equal(canonical, fromJsonInputFirst.ArgumentsJson);
    }

    [Fact]
    public void ChatRequestOptions_JsonContract_UsesToolAndPackExposureTokens() {
        var options = new ChatRequestOptions {
            EnabledTools = new[] { "dnsclientx_query", "ad_search" },
            DisabledTools = new[] { "filesystem_read" },
            EnabledPackIds = new[] { "dnsclientx", "active_directory" },
            DisabledPackIds = new[] { "filesystem" }
        };

        var json = JsonSerializer.Serialize(options, ChatServiceJsonContext.Default.ChatRequestOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var enabled = root.GetProperty("enabledTools").EnumerateArray().Select(static value => value.GetString()).ToArray();
        var disabled = root.GetProperty("disabledTools").EnumerateArray().Select(static value => value.GetString()).ToArray();
        var enabledPacks = root.GetProperty("enabledPackIds").EnumerateArray().Select(static value => value.GetString()).ToArray();
        var disabledPacks = root.GetProperty("disabledPackIds").EnumerateArray().Select(static value => value.GetString()).ToArray();

        Assert.Equal(new[] { "dnsclientx_query", "ad_search" }, enabled);
        Assert.Equal(new[] { "filesystem_read" }, disabled);
        Assert.Equal(new[] { "dnsclientx", "active_directory" }, enabledPacks);
        Assert.Equal(new[] { "filesystem" }, disabledPacks);
    }

    [Fact]
    public void ToolListMessage_JsonContract_RoundTripsExpandedToolDefinitionMetadata() {
        var message = new ToolListMessage {
            Kind = ChatServiceMessageKind.Response,
            Tools = new[] {
                new ToolDefinitionDto {
                    Name = "eventlog_timeline_query",
                    Description = "Reads event log timeline entries.",
                    DisplayName = "Event Timeline Query",
                    Category = "eventlog",
                    Tags = new[] { "eventlog", "remote", "timeline" },
                    PackId = "eventlog",
                    RoutingRole = "operational",
                    RoutingScope = "host",
                    RoutingOperation = "query",
                    RoutingEntity = "event",
                    RoutingRisk = "low",
                    RoutingSource = "explicit",
                    DomainIntentFamily = "host_operations",
                    DomainIntentActionId = "query_event_timeline",
                    PackName = "Event Log",
                    PackDescription = "Event log investigation tools.",
                    PackSourceKind = ToolPackSourceKind.OpenSource,
                    IsPackInfoTool = false,
                    IsEnvironmentDiscoverTool = true,
                    IsWriteCapable = false,
                    IsExecutionAware = true,
                    ExecutionContractId = "ix.tool-execution.v1",
                    ExecutionScope = "local_or_remote",
                    SupportsLocalExecution = true,
                    SupportsRemoteExecution = true,
                    SupportsTargetScoping = true,
                    TargetScopeArguments = new[] { "channel", "machine_name" },
                    SupportsRemoteHostTargeting = true,
                    RemoteHostArguments = new[] { "machine_name", "machine_names" },
                    IsSetupAware = true,
                    SetupToolName = "eventlog_channels_list",
                    IsHandoffAware = true,
                    HandoffTargetPackIds = new[] { "system" },
                    HandoffTargetToolNames = new[] { "system_info" },
                    IsRecoveryAware = true,
                    SupportsTransientRetry = true,
                    MaxRetryAttempts = 2,
                    RecoveryToolNames = new[] { "eventlog_channels_list" },
                    ParametersJson = """{"type":"object"}""",
                    RequiredArguments = new[] { "machine_name", "channel" },
                    Parameters = new[] {
                        new ToolParameterDto {
                            Name = "machine_name",
                            Type = "string",
                            Description = "Remote host name.",
                            Required = true,
                            EnumValues = new[] { "dc01", "dc02" },
                            DefaultJson = "\"dc01\"",
                            ExampleJson = "\"dc02\""
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize<ChatServiceMessage>(message, ChatServiceJsonContext.Default.ChatServiceMessage);
        var parsed = JsonSerializer.Deserialize(json, ChatServiceJsonContext.Default.ChatServiceMessage);
        var toolList = Assert.IsType<ToolListMessage>(parsed);
        var tool = Assert.Single(toolList.Tools);

        Assert.Equal("Event Timeline Query", tool.DisplayName);
        Assert.Equal("eventlog", tool.Category);
        Assert.Equal(ToolPackSourceKind.OpenSource, tool.PackSourceKind);
        Assert.Equal("operational", tool.RoutingRole);
        Assert.Equal("host", tool.RoutingScope);
        Assert.Equal("query", tool.RoutingOperation);
        Assert.Equal("event", tool.RoutingEntity);
        Assert.Equal("low", tool.RoutingRisk);
        Assert.Equal("explicit", tool.RoutingSource);
        Assert.Equal("host_operations", tool.DomainIntentFamily);
        Assert.Equal("query_event_timeline", tool.DomainIntentActionId);
        Assert.True(tool.IsExecutionAware);
        Assert.Equal("ix.tool-execution.v1", tool.ExecutionContractId);
        Assert.False(tool.IsPackInfoTool);
        Assert.True(tool.IsEnvironmentDiscoverTool);
        Assert.Equal("local_or_remote", tool.ExecutionScope);
        Assert.True(tool.SupportsLocalExecution);
        Assert.True(tool.SupportsRemoteExecution);
        Assert.Equal(new[] { "channel", "machine_name" }, tool.TargetScopeArguments);
        Assert.Equal(new[] { "machine_name", "machine_names" }, tool.RemoteHostArguments);
        Assert.True(tool.IsSetupAware);
        Assert.Equal("eventlog_channels_list", tool.SetupToolName);
        Assert.True(tool.IsHandoffAware);
        Assert.Equal(new[] { "system" }, tool.HandoffTargetPackIds);
        Assert.Equal(new[] { "system_info" }, tool.HandoffTargetToolNames);
        Assert.True(tool.IsRecoveryAware);
        Assert.True(tool.SupportsTransientRetry);
        Assert.Equal(2, tool.MaxRetryAttempts);
        Assert.Equal(new[] { "eventlog_channels_list" }, tool.RecoveryToolNames);
        var parameter = Assert.Single(tool.Parameters);
        Assert.Equal("machine_name", parameter.Name);
        Assert.Equal(new[] { "dc01", "dc02" }, parameter.EnumValues);
        Assert.Equal("\"dc01\"", parameter.DefaultJson);
        Assert.Equal("\"dc02\"", parameter.ExampleJson);
    }
}
