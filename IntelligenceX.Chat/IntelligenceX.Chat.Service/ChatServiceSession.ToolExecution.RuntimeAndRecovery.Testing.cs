using System;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    internal static object ResolveRetryProfileForTesting(ToolDefinition? definition) {
        return ResolveRetryProfile(definition);
    }

    internal static bool ShouldRetryToolCallForTesting(ToolOutputDto output, object retryProfile, int attemptIndex) {
        if (retryProfile is not ToolRetryProfile profile) {
            throw new ArgumentException("retryProfile must be a ChatServiceSession.ToolRetryProfile instance.", nameof(retryProfile));
        }

        return ShouldRetryToolCall(output, profile, attemptIndex);
    }
}
