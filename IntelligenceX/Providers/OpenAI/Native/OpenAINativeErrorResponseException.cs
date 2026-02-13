using System;
using System.Net;

namespace IntelligenceX.OpenAI.Native;

internal sealed class OpenAINativeErrorResponseException : InvalidOperationException {
    internal OpenAINativeErrorResponseException(string? message, string? rawText, string? code, string? param, HttpStatusCode statusCode,
        bool includeRawText)
        : this(message, rawText, code, param, statusCode, includeRawText, innerException: null) {
    }

    internal OpenAINativeErrorResponseException(string? message, string? rawText, string? code, string? param, HttpStatusCode statusCode,
        bool includeRawText, Exception? innerException)
        : base(string.IsNullOrWhiteSpace(message) ? "OpenAI request failed." : message, innerException) {
        ErrorCode = string.IsNullOrWhiteSpace(code) ? null : code;
        ErrorParam = string.IsNullOrWhiteSpace(param) ? null : param;
        StatusCode = statusCode;
        RawTextLength = rawText?.Length ?? 0;
        RawText = !string.IsNullOrWhiteSpace(rawText) ? Truncate(rawText!, 8192) : string.Empty;

        Data["openai:native_transport"] = true;
        Data["openai:status_code"] = (int)statusCode;
        Data["openai:raw_length"] = RawTextLength;
        Data["openai:raw_truncated"] = RawTextLength > 8192;
        Data["openai:raw_attached"] = includeRawText;
        if (ErrorCode is not null) {
            Data["openai:error_code"] = ErrorCode;
        }
        if (ErrorParam is not null) {
            Data["openai:error_param"] = ErrorParam;
        }
    }

    internal string? ErrorCode { get; }
    internal string? ErrorParam { get; }
    internal HttpStatusCode StatusCode { get; }
    internal int RawTextLength { get; }
    internal string RawText { get; }

    private static string Truncate(string value, int maxLength) {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength) {
            return value;
        }
        return value.Substring(0, maxLength) + "...(truncated)";
    }
}
