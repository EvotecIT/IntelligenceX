using System;

namespace IntelligenceX.Chat.Abstractions.Protocol;

/// <summary>
/// Shared heuristics for identifying recoverable chat-thread errors across app and service layers.
/// </summary>
public static class ChatThreadRecoveryHeuristics {
    /// <summary>
    /// Returns <c>true</c> when the exception message indicates a missing transport-managed thread.
    /// </summary>
    /// <param name="ex">Exception to inspect.</param>
    /// <returns><c>true</c> when thread recovery should be attempted; otherwise <c>false</c>.</returns>
    public static bool IsMissingTransportThreadError(Exception? ex) {
        var message = (ex?.Message ?? string.Empty).Trim();
        if (message.Length == 0) {
            return false;
        }

        return message.Contains("thread", StringComparison.OrdinalIgnoreCase)
               && message.Contains("not found", StringComparison.OrdinalIgnoreCase)
               && message.Contains("transport", StringComparison.OrdinalIgnoreCase);
    }
}
