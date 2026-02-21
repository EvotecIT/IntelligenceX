using System;
using System.Collections.Generic;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Shared classifier for deciding whether to retry a failed chat turn without tool schema metadata.
/// </summary>
public static class ToolSchemaRecoveryClassifier {
    /// <summary>
    /// Returns true when a failure should trigger a retry without tools/tool_choice metadata.
    /// </summary>
    /// <param name="exception">Thrown exception from provider call.</param>
    /// <param name="options">Chat options for the failed call.</param>
    public static bool ShouldRetryWithoutTools(Exception exception, ChatOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        if (exception is null) {
            return false;
        }

        if (options.Tools is not { Count: > 0 }) {
            return false;
        }

        if (TryShouldRetryWithoutToolsFromStructuredError(exception, out var shouldRetryStructured)) {
            return shouldRetryStructured;
        }

        // Compatibility fallback for providers that emit plain-text errors on an inner/aggregate exception.
        return TryShouldRetryWithoutToolsFromMessage(exception, out var shouldRetryMessage) && shouldRetryMessage;
    }

    private static bool TryShouldRetryWithoutToolsFromStructuredError(Exception ex, out bool shouldRetry) {
        shouldRetry = false;
        if (ex is null) {
            return false;
        }

        var pending = new Stack<Exception>();
        pending.Push(ex);
        while (pending.Count > 0) {
            var current = pending.Pop();

            if (TryReadNativeErrorDiagnostics(current, out var code, out var param)) {
                if (LooksLikeToolSchemaValidationCode(code, param) || LooksLikeContextWindowFailureCode(code)) {
                    shouldRetry = true;
                    return true;
                }

                // Structured diagnostics were present and explicitly non-retryable.
                shouldRetry = false;
                return true;
            }

            if (current is AggregateException aggregate) {
                foreach (var inner in aggregate.InnerExceptions) {
                    if (inner is not null) {
                        pending.Push(inner);
                    }
                }
            }

            if (current.InnerException is not null) {
                pending.Push(current.InnerException);
            }
        }

        return false;
    }

    private static bool TryShouldRetryWithoutToolsFromMessage(Exception ex, out bool shouldRetry) {
        shouldRetry = false;
        if (ex is null) {
            return false;
        }

        var pending = new Stack<Exception>();
        pending.Push(ex);
        while (pending.Count > 0) {
            var current = pending.Pop();
            var message = current.Message ?? string.Empty;
            if (message.Length > 0
                && (LooksLikeToolSchemaValidationMessage(message) || LooksLikeContextWindowFailureMessage(message))) {
                shouldRetry = true;
                return true;
            }

            if (current is AggregateException aggregate) {
                foreach (var inner in aggregate.InnerExceptions) {
                    if (inner is not null) {
                        pending.Push(inner);
                    }
                }
            }

            if (current.InnerException is not null) {
                pending.Push(current.InnerException);
            }
        }

        return false;
    }

    private static bool TryReadNativeErrorDiagnostics(Exception ex, out string errorCode, out string errorParam) {
        errorCode = string.Empty;
        errorParam = string.Empty;
        if (ex is null) {
            return false;
        }

        if (!(ex.Data?["openai:native_transport"] is bool marker && marker)) {
            return false;
        }

        errorCode = ((ex.Data?["openai:error_code"] as string) ?? string.Empty).Trim();
        errorParam = ((ex.Data?["openai:error_param"] as string) ?? string.Empty).Trim();
        return errorCode.Length > 0 || errorParam.Length > 0;
    }

    private static bool LooksLikeToolSchemaValidationCode(string errorCode, string errorParam) {
        var code = (errorCode ?? string.Empty).Trim();
        if (code.Length == 0) {
            return false;
        }

        var hasToolsPath = errorParam.IndexOf("tools", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!hasToolsPath) {
            return false;
        }

        if (code.IndexOf("unknown_parameter", StringComparison.OrdinalIgnoreCase) >= 0) {
            return true;
        }

        var missingRequiredParameter =
            code.IndexOf("missing_required_parameter", StringComparison.OrdinalIgnoreCase) >= 0
            || code.IndexOf("required_parameter_missing", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!missingRequiredParameter) {
            return false;
        }

        return errorParam.IndexOf(".name", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool LooksLikeContextWindowFailureCode(string errorCode) {
        var code = (errorCode ?? string.Empty).Trim();
        if (code.Length == 0) {
            return false;
        }

        return code.IndexOf("context_length", StringComparison.OrdinalIgnoreCase) >= 0
               || code.IndexOf("max_context", StringComparison.OrdinalIgnoreCase) >= 0
               || code.IndexOf("prompt_too_long", StringComparison.OrdinalIgnoreCase) >= 0
               || code.IndexOf("too_many_tokens", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool LooksLikeToolSchemaValidationMessage(string message) {
        var text = message ?? string.Empty;
        if (text.Length == 0) {
            return false;
        }

        return text.IndexOf("missing required parameter", StringComparison.OrdinalIgnoreCase) >= 0
               && text.IndexOf("tools", StringComparison.OrdinalIgnoreCase) >= 0
               && text.IndexOf(".name", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool LooksLikeContextWindowFailureMessage(string message) {
        var text = message ?? string.Empty;
        if (text.Length == 0) {
            return false;
        }

        return text.IndexOf("cannot truncate prompt with n_keep", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("n_ctx", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("context length", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("context window", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("maximum context length", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("prompt too long", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
