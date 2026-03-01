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
        string[] RetryableErrorCodes);

    internal static RetryProfileSnapshot ResolveRetryProfileForTesting(ToolDefinition? definition) {
        var profile = ResolveRetryProfile(definition);
        var retryableCodes = profile.RetryableErrorCodes is { Count: > 0 }
            ? profile.RetryableErrorCodes.ToArray()
            : Array.Empty<string>();
        return new RetryProfileSnapshot(
            MaxAttempts: profile.MaxAttempts,
            DelayBaseMs: profile.DelayBaseMs,
            RetryOnTimeout: profile.RetryOnTimeout,
            RetryOnTransport: profile.RetryOnTransport,
            RetryableErrorCodes: retryableCodes);
    }

    internal static bool ShouldRetryToolCallForTesting(ToolOutputDto output, RetryProfileSnapshot retryProfile, int attemptIndex) {
        var profile = new ToolRetryProfile(
            MaxAttempts: retryProfile.MaxAttempts,
            DelayBaseMs: retryProfile.DelayBaseMs,
            RetryOnTimeout: retryProfile.RetryOnTimeout,
            RetryOnTransport: retryProfile.RetryOnTransport,
            RetryableErrorCodes: retryProfile.RetryableErrorCodes ?? Array.Empty<string>());
        return ShouldRetryToolCall(output, profile, attemptIndex);
    }
}
