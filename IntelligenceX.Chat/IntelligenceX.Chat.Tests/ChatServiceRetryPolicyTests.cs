using System;
using System.Reflection;
using IntelligenceX.Chat.Abstractions.Protocol;
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

    private static object InvokeResolveRetryProfile(string toolName) {
        var profile = ResolveRetryProfileMethod.Invoke(null, new object?[] { toolName });
        return profile ?? throw new InvalidOperationException("ResolveRetryProfile returned null.");
    }

    private static bool InvokeShouldRetryToolCall(ToolOutputDto output, object profile, int attemptIndex) {
        var result = ShouldRetryToolCallMethod.Invoke(null, new object?[] { output, profile, attemptIndex });
        return Assert.IsType<bool>(result);
    }
}
