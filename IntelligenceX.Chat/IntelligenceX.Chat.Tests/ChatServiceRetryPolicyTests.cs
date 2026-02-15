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
            Output = "{\"error\":\"Unauthorized: access denied to domain controller.\"}",
            Ok = false,
            ErrorCode = "tool_exception",
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
    public void TryBuildProjectionArgsFallbackCall_StripsProjectionArgumentsAndKeepsToolArguments() {
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
        Assert.False(fallbackCall.Arguments.TryGetValue("top", out _));
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
