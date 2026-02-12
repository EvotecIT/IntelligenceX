using System;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Maps typed engine/tool failure contracts to stable tool error envelopes.
/// </summary>
public static class ToolFailureMapper {
    /// <summary>
    /// Maps a typed failure object to a tool error envelope.
    /// </summary>
    /// <typeparam name="TFailure">Failure payload type.</typeparam>
    /// <typeparam name="TCode">Failure code enum type.</typeparam>
    /// <param name="failure">Failure payload.</param>
    /// <param name="codeSelector">Selector for typed failure code.</param>
    /// <param name="messageSelector">Selector for failure message.</param>
    /// <param name="defaultMessage">Default message when failure/message is missing.</param>
    /// <param name="fallbackErrorCode">Fallback tool error code when mapping is unknown.</param>
    /// <returns>Serialized tool error envelope.</returns>
    public static string ErrorFromFailure<TFailure, TCode>(
        TFailure? failure,
        Func<TFailure, TCode> codeSelector,
        Func<TFailure, string?> messageSelector,
        string defaultMessage,
        string fallbackErrorCode = "query_failed")
        where TFailure : class {
        if (codeSelector is null) {
            throw new ArgumentNullException(nameof(codeSelector));
        }
        if (messageSelector is null) {
            throw new ArgumentNullException(nameof(messageSelector));
        }

        var codeName = failure is null ? null : codeSelector(failure)?.ToString();
        var errorCode = MapCode(codeName, fallbackErrorCode);

        var message = failure is null ? defaultMessage : messageSelector(failure);
        if (string.IsNullOrWhiteSpace(message)) {
            message = string.IsNullOrWhiteSpace(defaultMessage) ? "Operation failed." : defaultMessage;
        }

        return ToolResponse.Error(errorCode, message!);
    }

    /// <summary>
    /// Maps typed failure code names to stable tool error codes.
    /// </summary>
    /// <param name="codeName">Typed failure code name.</param>
    /// <param name="fallbackErrorCode">Fallback error code when mapping is unknown.</param>
    /// <returns>Stable tool error code.</returns>
    public static string MapCode(string? codeName, string fallbackErrorCode = "query_failed") {
        var normalized = Normalize(codeName);
        return normalized switch {
            "invalidrequest" => "invalid_argument",
            "invalidargument" => "invalid_argument",
            "cancelled" => "cancelled",
            "canceled" => "cancelled",
            "platformnotsupported" => "unsupported_platform",
            "unsupportedplatform" => "unsupported_platform",
            "queryfailed" => "query_failed",
            "notfound" => "not_found",
            "accessdenied" => "access_denied",
            "timeout" => "timeout",
            "timedout" => "timeout",
            "ioerror" => "io_error",
            "iofailure" => "io_error",
            "processerror" => "process_error",
            _ => string.IsNullOrWhiteSpace(fallbackErrorCode) ? "query_failed" : fallbackErrorCode
        };
    }

    private static string Normalize(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var chars = value.Trim();
        Span<char> buffer = stackalloc char[chars.Length];
        var j = 0;
        for (var i = 0; i < chars.Length; i++) {
            var c = chars[i];
            if (char.IsLetterOrDigit(c)) {
                buffer[j++] = char.ToLowerInvariant(c);
            }
        }

        return new string(buffer[..j]);
    }
}
