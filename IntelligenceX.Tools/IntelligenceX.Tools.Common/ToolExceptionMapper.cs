using System;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Maps runtime exceptions to stable tool error envelopes and sanitizes user-facing messages.
/// </summary>
public static class ToolExceptionMapper {
    private const int MaxErrorMessageLength = 300;

    /// <summary>
    /// Maps common runtime exceptions to stable tool error envelopes.
    /// </summary>
    public static string ErrorFromException(
        Exception exception,
        string defaultMessage,
        string unauthorizedMessage,
        string timeoutMessage,
        string fallbackErrorCode = "query_failed",
        string invalidOperationErrorCode = "invalid_argument") {
        if (exception is null) {
            throw new ArgumentNullException(nameof(exception));
        }

        var safeDefault = string.IsNullOrWhiteSpace(defaultMessage)
            ? "Operation failed."
            : defaultMessage.Trim();

        return exception switch {
            OperationCanceledException => ToolResponse.Error("cancelled", "Operation was cancelled.", isTransient: true),
            ArgumentException => ToolResponse.Error("invalid_argument", SanitizeErrorMessage(exception.Message, safeDefault)),
            InvalidOperationException => ToolResponse.Error(
                string.IsNullOrWhiteSpace(invalidOperationErrorCode) ? "invalid_argument" : invalidOperationErrorCode,
                SanitizeErrorMessage(exception.Message, safeDefault)),
            FormatException => ToolResponse.Error("invalid_argument", SanitizeErrorMessage(exception.Message, safeDefault)),
            UnauthorizedAccessException => ToolResponse.Error(
                "access_denied",
                SanitizeErrorMessage(exception.Message, string.IsNullOrWhiteSpace(unauthorizedMessage) ? "Access denied." : unauthorizedMessage)),
            TimeoutException => ToolResponse.Error(
                "timeout",
                SanitizeErrorMessage(exception.Message, string.IsNullOrWhiteSpace(timeoutMessage) ? "Operation timed out." : timeoutMessage),
                isTransient: true),
            NotSupportedException => ToolResponse.Error("not_supported", SanitizeErrorMessage(exception.Message, safeDefault)),
            _ => ToolResponse.Error(
                string.IsNullOrWhiteSpace(fallbackErrorCode) ? "query_failed" : fallbackErrorCode,
                safeDefault)
        };
    }

    /// <summary>
    /// Compacts and bounds an error message to keep tool envelopes stable/safe.
    /// </summary>
    public static string SanitizeErrorMessage(string? message, string fallback) {
        var safeFallback = string.IsNullOrWhiteSpace(fallback) ? "Operation failed." : fallback.Trim();
        if (string.IsNullOrWhiteSpace(message)) {
            return safeFallback;
        }

        var compact = message
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();

        while (compact.Contains("  ", StringComparison.Ordinal)) {
            compact = compact.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (compact.Length == 0) {
            return safeFallback;
        }

        return compact.Length <= MaxErrorMessageLength
            ? compact
            : compact.Substring(0, MaxErrorMessageLength).TrimEnd() + "...";
    }
}
