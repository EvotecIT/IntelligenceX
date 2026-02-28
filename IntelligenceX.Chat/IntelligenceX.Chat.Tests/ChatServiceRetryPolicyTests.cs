using System;
using System.Reflection;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Covers retry policy boundaries for transient/permanent tool failures.
/// </summary>
public sealed class ChatServiceRetryPolicyTests {
    private static readonly Type ChatServiceSessionType =
        Type.GetType("IntelligenceX.Chat.Service.ChatServiceSession, IntelligenceX.Chat.Service")
        ?? throw new InvalidOperationException("ChatServiceSession type not found.");

    private static readonly MethodInfo ResolveRetryProfileMethod = ChatServiceSessionType.GetMethod(
        "ResolveRetryProfile",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ResolveRetryProfile not found.");

    private static readonly MethodInfo ShouldRetryToolCallMethod = ChatServiceSessionType.GetMethod(
        "ShouldRetryToolCall",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldRetryToolCall not found.");
    private static readonly MethodInfo IsProjectionViewArgumentFailureMethod = ChatServiceSessionType.GetMethod(
        "IsProjectionViewArgumentFailure",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("IsProjectionViewArgumentFailure not found.");
    private static readonly MethodInfo TryBuildProjectionArgsFallbackCallMethod = ChatServiceSessionType.GetMethod(
        "TryBuildProjectionArgsFallbackCall",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("TryBuildProjectionArgsFallbackCall not found.");

    [Fact]
    public void ShouldRetryToolCall_DoesNotRetryPermanentAccessDeniedFailure() {
        var profile = InvokeResolveRetryProfile("ad_replication_summary");
        var output = new ToolOutputDto {
            CallId = "call-1",
            Output = "{\"error_code\":\"permission_denied\",\"error\":\"Unauthorized: access denied to domain controller.\"}",
            Ok = false,
            ErrorCode = "permission_denied",
            Error = "Unauthorized: access denied to domain controller.",
            IsTransient = true
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetryToolCall_RetriesTimeoutBeforeFinalAttemptOnly() {
        var profile = InvokeResolveRetryProfile("ad_replication_summary");
        var output = new ToolOutputDto {
            CallId = "call-2",
            Output = "{\"error\":\"Tool timed out.\"}",
            Ok = false,
            ErrorCode = "tool_timeout",
            Error = "Tool timed out."
        };

        var shouldRetryFirstAttempt = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);
        var shouldRetryFinalAttempt = InvokeShouldRetryToolCall(output, profile, attemptIndex: 1);

        Assert.True(shouldRetryFirstAttempt);
        Assert.False(shouldRetryFinalAttempt);
    }

    [Fact]
    public void ShouldRetryToolCall_DoesNotRetryTransportSignalsForFsProfile() {
        var profile = InvokeResolveRetryProfile("fs_read_file");
        var output = new ToolOutputDto {
            CallId = "call-3",
            Output = "{\"error\":\"Service temporarily unavailable.\"}",
            Ok = false,
            ErrorCode = "transport_unavailable",
            Error = "Service temporarily unavailable."
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetryToolCall_RetriesTimeoutForDomainDetectiveProfile() {
        var profile = InvokeResolveRetryProfile("domaindetective_domain_summary");
        var output = new ToolOutputDto {
            CallId = "call-3c",
            Output = "{\"error\":\"Tool timed out.\"}",
            Ok = false,
            ErrorCode = "tool_timeout",
            Error = "Tool timed out."
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetryToolCall_RetriesTransportForDnsClientXProfile() {
        var profile = InvokeResolveRetryProfile("dnsclientx_query");
        var output = new ToolOutputDto {
            CallId = "call-3d",
            Output = "{\"error\":\"Service temporarily unavailable.\"}",
            Ok = false,
            ErrorCode = "transport_unavailable",
            Error = "Service temporarily unavailable."
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetryToolCall_RetriesTransportForTestimoXProfile() {
        var profile = InvokeResolveRetryProfile("testimox_rules_run");
        var output = new ToolOutputDto {
            CallId = "call-3e",
            Output = "{\"error\":\"Upstream connection reset.\"}",
            Ok = false,
            ErrorCode = "transport_unavailable",
            Error = "Upstream connection reset."
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetryToolCall_RetriesTimeoutForComputerXPrefixProfile() {
        var profile = InvokeResolveRetryProfile("computerx_inventory_snapshot");
        var output = new ToolOutputDto {
            CallId = "call-3f",
            Output = "{\"error\":\"Tool timed out.\"}",
            Ok = false,
            ErrorCode = "tool_timeout",
            Error = "Tool timed out."
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetryToolCall_DoesNotRetryDomainScopeGuardrailFailure() {
        var profile = InvokeResolveRetryProfile("ad_replication_summary");
        var output = new ToolOutputDto {
            CallId = "call-3b",
            Output = "{\"error_code\":\"domain_scope_host_guardrail\",\"error\":\"Blocked host target.\"}",
            Ok = false,
            ErrorCode = "domain_scope_host_guardrail",
            Error = "Blocked host target.",
            IsTransient = true
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.False(shouldRetry);
    }

    [Fact]
    public void IsProjectionViewArgumentFailure_DetectsUnsupportedProjectionColumns() {
        var output = new ToolOutputDto {
            CallId = "call-4",
            Output = "{\"ok\":false,\"error_code\":\"invalid_argument\",\"error\":\"columns contains unsupported value 'bad_column'.\"}",
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "columns contains unsupported value 'bad_column'."
        };

        var detected = InvokeIsProjectionViewArgumentFailure(output);

        Assert.True(detected);
    }

    [Fact]
    public void IsProjectionViewArgumentFailure_DoesNotMatchUnrelatedPermanentErrors() {
        var output = new ToolOutputDto {
            CallId = "call-5",
            Output = "{\"ok\":false,\"error_code\":\"tool_exception\",\"error\":\"Access denied.\"}",
            Ok = false,
            ErrorCode = "tool_exception",
            Error = "Access denied."
        };

        var detected = InvokeIsProjectionViewArgumentFailure(output);

        Assert.False(detected);
    }

    [Fact]
    public void TryBuildProjectionArgsFallbackCall_StripsProjectionFormattingArgumentsAndPreservesTopByDefault() {
        var call = new ToolCall(
            callId: "call-6",
            name: "eventlog_top_events",
            input: null,
            arguments: new JsonObject()
                .Add("log_name", "System")
                .Add("columns", new JsonArray().Add("event_id"))
                .Add("sort_by", "event_id")
                .Add("sort_direction", "desc")
                .Add("top", 5),
            raw: new JsonObject());
        var output = new ToolOutputDto {
            CallId = call.CallId,
            Output = "{\"ok\":false,\"error_code\":\"invalid_argument\",\"error\":\"sort_by must be one of: event_id, level.\"}",
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "sort_by must be one of: event_id, level."
        };

        var args = new object?[] { call, output, null, null };
        var built = TryBuildProjectionArgsFallbackCallMethod.Invoke(null, args);
        var fallbackCall = Assert.IsType<ToolCall>(args[2]);

        Assert.True(Assert.IsType<bool>(built));
        Assert.NotNull(args[3]);
        Assert.NotNull(fallbackCall.Arguments);
        Assert.Equal("System", fallbackCall.Arguments!.GetString("log_name"));
        Assert.False(fallbackCall.Arguments.TryGetValue("columns", out _));
        Assert.False(fallbackCall.Arguments.TryGetValue("sort_by", out _));
        Assert.False(fallbackCall.Arguments.TryGetValue("sort_direction", out _));
        Assert.Equal(5, fallbackCall.Arguments.GetInt64("top"));
    }

    [Fact]
    public void TryBuildProjectionArgsFallbackCall_ParsesInputArgumentsWhenStructuredArgumentsMissing() {
        var call = new ToolCall(
            callId: "call-6b",
            name: "ad_search",
            input: """
                   {"query":"przemyslaw.klys","log_name":"System","columns":["objectSid"],"sort_by":"sAMAccountName","sort_direction":"asc","top":10}
                   """,
            arguments: null,
            raw: new JsonObject());
        var output = new ToolOutputDto {
            CallId = call.CallId,
            Output = "{\"ok\":false,\"error_code\":\"invalid_argument\",\"error\":\"columns contains unsupported value 'objectSid'.\"}",
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "columns contains unsupported value 'objectSid'."
        };

        var args = new object?[] { call, output, null, null };
        var built = TryBuildProjectionArgsFallbackCallMethod.Invoke(null, args);
        var fallbackCall = Assert.IsType<ToolCall>(args[2]);

        Assert.True(Assert.IsType<bool>(built));
        Assert.NotNull(fallbackCall.Arguments);
        Assert.Equal("przemyslaw.klys", fallbackCall.Arguments!.GetString("query"));
        Assert.Equal("System", fallbackCall.Arguments.GetString("log_name"));
        Assert.False(fallbackCall.Arguments.TryGetValue("columns", out _));
        Assert.False(fallbackCall.Arguments.TryGetValue("sort_by", out _));
        Assert.False(fallbackCall.Arguments.TryGetValue("sort_direction", out _));
        Assert.Equal(10, fallbackCall.Arguments.GetInt64("top"));
    }

    [Fact]
    public void TryBuildProjectionArgsFallbackCall_DropsTopAsLastResortForProjectionEnvelopeFailures() {
        var call = new ToolCall(
            callId: "call-7",
            name: "eventlog_top_events",
            input: null,
            arguments: new JsonObject()
                .Add("log_name", "System")
                .Add("top", 5),
            raw: new JsonObject());
        var output = new ToolOutputDto {
            CallId = call.CallId,
            Output = "{\"ok\":false,\"error_code\":\"invalid_argument\",\"error\":\"Failed to build table view response envelope.\"}",
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "Failed to build table view response envelope."
        };

        var args = new object?[] { call, output, null, null };
        var built = TryBuildProjectionArgsFallbackCallMethod.Invoke(null, args);
        var fallbackCall = Assert.IsType<ToolCall>(args[2]);

        Assert.True(Assert.IsType<bool>(built));
        Assert.NotNull(fallbackCall.Arguments);
        Assert.False(fallbackCall.Arguments!.TryGetValue("top", out _));
    }

    [Fact]
    public void TryBuildProjectionArgsFallbackCall_DropsTopOnExplicitTopValidationFailure() {
        var call = new ToolCall(
            callId: "call-8",
            name: "eventlog_top_events",
            input: null,
            arguments: new JsonObject()
                .Add("log_name", "System")
                .Add("top", 5000),
            raw: new JsonObject());
        var output = new ToolOutputDto {
            CallId = call.CallId,
            Output = "{\"ok\":false,\"error_code\":\"invalid_argument\",\"error\":\"top must be between 1 and 100 for table view response envelope.\"}",
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "top must be between 1 and 100 for table view response envelope."
        };

        var args = new object?[] { call, output, null, null };
        var built = TryBuildProjectionArgsFallbackCallMethod.Invoke(null, args);
        var fallbackCall = Assert.IsType<ToolCall>(args[2]);

        Assert.True(Assert.IsType<bool>(built));
        Assert.NotNull(fallbackCall.Arguments);
        Assert.Equal("System", fallbackCall.Arguments!.GetString("log_name"));
        Assert.False(fallbackCall.Arguments.TryGetValue("top", out _));
    }

    [Fact]
    public void TryBuildProjectionArgsFallbackCall_DoesNotDropTopOnIncidentalTopText() {
        var call = new ToolCall(
            callId: "call-8b",
            name: "eventlog_top_events",
            input: null,
            arguments: new JsonObject()
                .Add("log_name", "System")
                .Add("top", 5),
            raw: new JsonObject());
        var output = new ToolOutputDto {
            CallId = call.CallId,
            Output = "{\"ok\":false,\"error_code\":\"invalid_argument\",\"error\":\"Failed to query top events; invalid filter expression.\"}",
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "Failed to query top events; invalid filter expression."
        };

        var args = new object?[] { call, output, null, null };
        var built = TryBuildProjectionArgsFallbackCallMethod.Invoke(null, args);
        var fallbackCall = Assert.IsType<ToolCall>(args[2]);

        Assert.False(Assert.IsType<bool>(built));
        Assert.NotNull(fallbackCall.Arguments);
        Assert.Equal(5, fallbackCall.Arguments!.GetInt64("top"));
    }

    /// <summary>
    /// Ensures projection fallback keeps valid sort/top intent while pruning only unsupported columns.
    /// </summary>
    [Fact]
    public void TryBuildProjectionArgsFallbackCall_SelectivelyPrunesUnsupportedColumnsUsingMetadata() {
        var call = new ToolCall(
            callId: "call-9",
            name: "eventlog_top_events",
            input: null,
            arguments: new JsonObject()
                .Add("log_name", "System")
                .Add("columns", new JsonArray().Add("event_id").Add("unknown_column"))
                .Add("sort_by", "event_id")
                .Add("sort_direction", "desc")
                .Add("top", 5),
            raw: new JsonObject());
        var output = new ToolOutputDto {
            CallId = call.CallId,
            Output = """
                     {
                       "ok": false,
                       "error_code": "invalid_argument",
                       "error": "columns contains unsupported value 'unknown_column'.",
                       "meta": {
                         "available_columns": ["event_id", "level"]
                       }
                     }
                     """,
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "columns contains unsupported value 'unknown_column'."
        };

        var args = new object?[] { call, output, null, null };
        var built = TryBuildProjectionArgsFallbackCallMethod.Invoke(null, args);
        var fallbackCall = Assert.IsType<ToolCall>(args[2]);

        Assert.True(Assert.IsType<bool>(built));
        Assert.NotNull(fallbackCall.Arguments);
        Assert.Equal("System", fallbackCall.Arguments!.GetString("log_name"));
        var columns = fallbackCall.Arguments.GetArray("columns");
        Assert.NotNull(columns);
        Assert.Single(columns!);
        Assert.Equal("event_id", columns[0].AsString());
        Assert.Equal("event_id", fallbackCall.Arguments.GetString("sort_by"));
        Assert.Equal("desc", fallbackCall.Arguments.GetString("sort_direction"));
        Assert.Equal(5, fallbackCall.Arguments.GetInt64("top"));
    }

    /// <summary>
    /// Ensures selective projection pruning still works when available columns are surfaced under failure.meta in camelCase.
    /// </summary>
    [Fact]
    public void TryBuildProjectionArgsFallbackCall_SelectivelyPrunesUsingFailureMetaCamelCaseColumns() {
        var call = new ToolCall(
            callId: "call-9b",
            name: "eventlog_top_events",
            input: null,
            arguments: new JsonObject()
                .Add("log_name", "System")
                .Add("columns", new JsonArray().Add("event_id").Add("unknown_column"))
                .Add("sort_by", "event_id")
                .Add("sort_direction", "desc")
                .Add("top", 5),
            raw: new JsonObject());
        var output = new ToolOutputDto {
            CallId = call.CallId,
            Output = """
                     {
                       "ok": false,
                       "error_code": "invalid_argument",
                       "error": "columns contains unsupported value 'unknown_column'.",
                       "failure": {
                         "meta": {
                           "availableColumns": ["event_id", "level"]
                         }
                       }
                     }
                     """,
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "columns contains unsupported value 'unknown_column'."
        };

        var args = new object?[] { call, output, null, null };
        var built = TryBuildProjectionArgsFallbackCallMethod.Invoke(null, args);
        var fallbackCall = Assert.IsType<ToolCall>(args[2]);

        Assert.True(Assert.IsType<bool>(built));
        Assert.NotNull(fallbackCall.Arguments);
        var columns = fallbackCall.Arguments!.GetArray("columns");
        Assert.NotNull(columns);
        Assert.Single(columns!);
        Assert.Equal("event_id", columns[0].AsString());
        Assert.Equal("event_id", fallbackCall.Arguments.GetString("sort_by"));
        Assert.Equal("desc", fallbackCall.Arguments.GetString("sort_direction"));
        Assert.Equal(5, fallbackCall.Arguments.GetInt64("top"));
    }

    /// <summary>
    /// Ensures we still apply conservative projection reset when metadata does not identify a specific bad argument.
    /// </summary>
    [Fact]
    public void TryBuildProjectionArgsFallbackCall_FallsBackConservativelyWhenMetadataShowsNoSpecificInvalidProjectionArgs() {
        var call = new ToolCall(
            callId: "call-10",
            name: "eventlog_top_events",
            input: null,
            arguments: new JsonObject()
                .Add("log_name", "System")
                .Add("columns", new JsonArray().Add("event_id"))
                .Add("sort_by", "event_id")
                .Add("sort_direction", "desc")
                .Add("top", 5),
            raw: new JsonObject());
        var output = new ToolOutputDto {
            CallId = call.CallId,
            Output = """
                     {
                       "ok": false,
                       "error_code": "invalid_argument",
                       "error": "Failed to build table view response envelope.",
                       "meta": {
                         "available_columns": ["event_id", "level"]
                       }
                     }
                     """,
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "Failed to build table view response envelope."
        };

        var args = new object?[] { call, output, null, null };
        var built = TryBuildProjectionArgsFallbackCallMethod.Invoke(null, args);
        var fallbackCall = Assert.IsType<ToolCall>(args[2]);

        Assert.True(Assert.IsType<bool>(built));
        Assert.NotNull(fallbackCall.Arguments);
        Assert.False(fallbackCall.Arguments!.TryGetValue("columns", out _));
        Assert.False(fallbackCall.Arguments.TryGetValue("sort_by", out _));
        Assert.False(fallbackCall.Arguments.TryGetValue("sort_direction", out _));
        Assert.Equal(5, fallbackCall.Arguments.GetInt64("top"));
    }

    private static object InvokeResolveRetryProfile(string toolName) {
        var profile = ResolveRetryProfileMethod.Invoke(null, new object?[] { toolName });
        return profile ?? throw new InvalidOperationException("ResolveRetryProfile returned null.");
    }

    private static bool InvokeShouldRetryToolCall(ToolOutputDto output, object profile, int attemptIndex) {
        var result = ShouldRetryToolCallMethod.Invoke(null, new object?[] { output, profile, attemptIndex });
        return Assert.IsType<bool>(result);
    }

    private static bool InvokeIsProjectionViewArgumentFailure(ToolOutputDto output) {
        var result = IsProjectionViewArgumentFailureMethod.Invoke(null, new object?[] { output });
        return Assert.IsType<bool>(result);
    }
}
