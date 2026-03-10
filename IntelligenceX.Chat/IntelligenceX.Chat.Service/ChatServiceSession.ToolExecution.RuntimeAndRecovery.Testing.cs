using System;
using System.Linq;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    internal readonly record struct RetryProfileSnapshot(
        int MaxAttempts,
        int DelayBaseMs,
        bool RetryOnTimeout,
        bool RetryOnTransport,
        string[] RetryableErrorCodes,
        string[] RecoveryToolNames,
        string[] AlternateEngineIds);

    internal static RetryProfileSnapshot ResolveRetryProfileForTesting(ToolDefinition? definition) {
        var profile = ResolveRetryProfile(definition);
        var retryableCodes = profile.RetryableErrorCodes is { Count: > 0 }
            ? profile.RetryableErrorCodes.ToArray()
            : Array.Empty<string>();
        var recoveryToolNames = profile.RecoveryToolNames is { Count: > 0 }
            ? profile.RecoveryToolNames.ToArray()
            : Array.Empty<string>();
        var alternateEngineIds = profile.AlternateEngineIds is { Count: > 0 }
            ? profile.AlternateEngineIds.ToArray()
            : Array.Empty<string>();
        return new RetryProfileSnapshot(
            MaxAttempts: profile.MaxAttempts,
            DelayBaseMs: profile.DelayBaseMs,
            RetryOnTimeout: profile.RetryOnTimeout,
            RetryOnTransport: profile.RetryOnTransport,
            RetryableErrorCodes: retryableCodes,
            RecoveryToolNames: recoveryToolNames,
            AlternateEngineIds: alternateEngineIds);
    }

    internal static bool ShouldRetryToolCallForTesting(ToolOutputDto output, RetryProfileSnapshot retryProfile, int attemptIndex) {
        var profile = new ToolRetryProfile(
            MaxAttempts: retryProfile.MaxAttempts,
            DelayBaseMs: retryProfile.DelayBaseMs,
            RetryOnTimeout: retryProfile.RetryOnTimeout,
            RetryOnTransport: retryProfile.RetryOnTransport,
            RetryableErrorCodes: retryProfile.RetryableErrorCodes ?? Array.Empty<string>(),
            RecoveryToolNames: retryProfile.RecoveryToolNames ?? Array.Empty<string>(),
            AlternateEngineIds: retryProfile.AlternateEngineIds ?? Array.Empty<string>());
        return ShouldRetryToolCall(output, profile, attemptIndex);
    }

    internal static bool ShouldAttemptRecoveryHelperToolsForTesting(
        ToolOutputDto output,
        RetryProfileSnapshot retryProfile,
        bool recoveryHelperAttempted = false) {
        var profile = new ToolRetryProfile(
            MaxAttempts: retryProfile.MaxAttempts,
            DelayBaseMs: retryProfile.DelayBaseMs,
            RetryOnTimeout: retryProfile.RetryOnTimeout,
            RetryOnTransport: retryProfile.RetryOnTransport,
            RetryableErrorCodes: retryProfile.RetryableErrorCodes ?? Array.Empty<string>(),
            RecoveryToolNames: retryProfile.RecoveryToolNames ?? Array.Empty<string>(),
            AlternateEngineIds: retryProfile.AlternateEngineIds ?? Array.Empty<string>());
        return ShouldAttemptRecoveryHelperTools(output, profile, recoveryHelperAttempted);
    }

    internal static bool TryBuildAlternateEngineFallbackCallForTesting(
        ToolCall call,
        ToolDefinition? definition,
        RetryProfileSnapshot retryProfile,
        out ToolCall fallbackCall,
        out string selectedEngineId) {
        var profile = new ToolRetryProfile(
            MaxAttempts: retryProfile.MaxAttempts,
            DelayBaseMs: retryProfile.DelayBaseMs,
            RetryOnTimeout: retryProfile.RetryOnTimeout,
            RetryOnTransport: retryProfile.RetryOnTransport,
            RetryableErrorCodes: retryProfile.RetryableErrorCodes ?? Array.Empty<string>(),
            RecoveryToolNames: retryProfile.RecoveryToolNames ?? Array.Empty<string>(),
            AlternateEngineIds: retryProfile.AlternateEngineIds ?? Array.Empty<string>());
        return TryBuildAlternateEngineFallbackCall(call, definition, profile, out fallbackCall, out selectedEngineId);
    }

    internal bool TryBuildPreferredHealthyAlternateEngineCallForTesting(
        string threadId,
        ToolCall call,
        ToolDefinition? definition,
        RetryProfileSnapshot retryProfile,
        out ToolCall preferredCall,
        out string selectedEngineId) {
        var profile = new ToolRetryProfile(
            MaxAttempts: retryProfile.MaxAttempts,
            DelayBaseMs: retryProfile.DelayBaseMs,
            RetryOnTimeout: retryProfile.RetryOnTimeout,
            RetryOnTransport: retryProfile.RetryOnTransport,
            RetryableErrorCodes: retryProfile.RetryableErrorCodes ?? Array.Empty<string>(),
            RecoveryToolNames: retryProfile.RecoveryToolNames ?? Array.Empty<string>(),
            AlternateEngineIds: retryProfile.AlternateEngineIds ?? Array.Empty<string>());
        return TryBuildPreferredHealthyAlternateEngineCall(threadId, call, definition, profile, out preferredCall, out selectedEngineId);
    }

    internal Task<ToolOutputDto> ExecuteToolAsyncForTesting(
        string threadId,
        string userRequest,
        ToolCall call,
        int toolTimeoutSeconds,
        CancellationToken cancellationToken) {
        return ExecuteToolAsync(threadId, userRequest, call, toolTimeoutSeconds, cancellationToken);
    }
}
