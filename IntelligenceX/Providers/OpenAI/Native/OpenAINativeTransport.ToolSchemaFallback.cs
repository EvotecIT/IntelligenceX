using System;
using System.Collections.Generic;

namespace IntelligenceX.OpenAI.Native;

internal sealed partial class OpenAINativeTransport {
    private static bool TryGetToolSchemaKeyFallback(Exception? ex, out ToolSchemaKey fallbackKey) {
        fallbackKey = ToolSchemaKey.Parameters;
        if (ex is null) {
            return false;
        }

        // The transport typically throws InvalidOperationException for server validation errors,
        // but callers can wrap it (including AggregateException). Unwrap defensively.
        var pending = new Stack<Exception>();
        pending.Push(ex);
        while (pending.Count > 0) {
            var current = pending.Pop();

            if (current is InvalidOperationException ioe &&
                TryGetToolSchemaKeyFallback(ioe, out fallbackKey)) {
                return true;
            }

            if (current is AggregateException agg) {
                foreach (var inner in agg.InnerExceptions) {
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

    private static bool TryGetToolSchemaKeyFallback(InvalidOperationException? ex, out ToolSchemaKey fallbackKey) {
        fallbackKey = ToolSchemaKey.Parameters;
        if (ex is null) {
            return false;
        }

        string? code;
        string? param;

        if (ex is OpenAINativeErrorResponseException native) {
            // Only retry for response-derived validation failures. This prevents tool schema fallback from masking
            // unrelated local errors that happen to throw InvalidOperationException.
            if (native.StatusCode != System.Net.HttpStatusCode.BadRequest &&
                (int)native.StatusCode != 422 /* Unprocessable Entity (not available in older TFMs) */) {
                return false;
            }

            code = native.ErrorCode;
            param = native.ErrorParam;
        } else {
            // Allow fallback when the original native exception type is lost (e.g., rewrapped) but diagnostic data is preserved.
            // We require a transport marker to avoid false positives from arbitrary InvalidOperationException instances.
            if (!(ex.Data?["openai:native_transport"] is bool marker && marker)) {
                return false;
            }

            code = ex.Data?["openai:error_code"] as string;
            param = ex.Data?["openai:error_param"] as string;
        }

        if (string.IsNullOrWhiteSpace(param)) {
            return false;
        }
        if (!param!.TrimStart().StartsWith("tools", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        // If the server provided a structured code, require it to match an unknown-parameter style error.
        if (!string.IsNullOrWhiteSpace(code) &&
            code!.IndexOf("unknown_parameter", StringComparison.OrdinalIgnoreCase) < 0) {
            return false;
        }

        return TryGetToolSchemaKeyFallback(param, out fallbackKey);
    }

    private static bool TryGetToolSchemaKeyFallback(string? message, out ToolSchemaKey fallbackKey) {
        // Server error messages vary; the stable signal is the field path that was rejected:
        // - tools[<n>].parameters
        // - tools[<n>].input_schema
        // - tools.<n>.parameters (seen in some variants)
        // - tools.<n>.input_schema
        fallbackKey = ToolSchemaKey.Parameters;
        if (string.IsNullOrWhiteSpace(message)) {
            return false;
        }

        var text = message!;
        var idx = 0;
        while (idx < text.Length) {
            // Bracket form: tools[12].parameters
            var start = text.IndexOf("tools[", idx, StringComparison.OrdinalIgnoreCase);
            if (start < 0) {
                break;
            }

            var i = start + "tools[".Length;
            if (i >= text.Length || !char.IsDigit(text[i])) {
                idx = i;
                continue;
            }
            while (i < text.Length && char.IsDigit(text[i])) {
                i++;
            }
            if (i >= text.Length || text[i] != ']') {
                idx = i;
                continue;
            }
            i++;
            if (i >= text.Length || text[i] != '.') {
                idx = i;
                continue;
            }
            i++;

            if (TryReadIdentifier(text, i, out var identifier)) {
                if (string.Equals(identifier, "parameters", StringComparison.OrdinalIgnoreCase)) {
                    fallbackKey = ToolSchemaKey.InputSchema;
                    return true;
                }
                if (string.Equals(identifier, "input_schema", StringComparison.OrdinalIgnoreCase)) {
                    fallbackKey = ToolSchemaKey.Parameters;
                    return true;
                }
            }

            idx = i;
        }

        idx = 0;
        while (idx < text.Length) {
            // Dot form: tools.12.parameters
            var start = text.IndexOf("tools.", idx, StringComparison.OrdinalIgnoreCase);
            if (start < 0) {
                break;
            }

            var i = start + "tools.".Length;
            if (i >= text.Length || !char.IsDigit(text[i])) {
                idx = i;
                continue;
            }
            while (i < text.Length && char.IsDigit(text[i])) {
                i++;
            }
            if (i >= text.Length || text[i] != '.') {
                idx = i;
                continue;
            }
            i++;

            if (TryReadIdentifier(text, i, out var identifier)) {
                if (string.Equals(identifier, "parameters", StringComparison.OrdinalIgnoreCase)) {
                    fallbackKey = ToolSchemaKey.InputSchema;
                    return true;
                }
                if (string.Equals(identifier, "input_schema", StringComparison.OrdinalIgnoreCase)) {
                    fallbackKey = ToolSchemaKey.Parameters;
                    return true;
                }
            }

            idx = i;
        }

        return false;
    }

    private static bool TryReadIdentifier(string text, int startIndex, out string identifier) {
        identifier = string.Empty;
        if (startIndex < 0 || startIndex >= text.Length) {
            return false;
        }
        var i = startIndex;
        while (i < text.Length) {
            var c = text[i];
            if (!(char.IsLetterOrDigit(c) || c == '_')) {
                break;
            }
            i++;
        }
        if (i == startIndex) {
            return false;
        }
        identifier = text.Substring(startIndex, i - startIndex);
        return true;
    }
}
