using System;
using System.Net;

namespace IntelligenceX.OpenAI.Native;

internal sealed class OpenAINativeErrorResponseException : InvalidOperationException {
    internal OpenAINativeErrorResponseException(string? message, string rawText, string? code, string? param, HttpStatusCode statusCode)
        : base(string.IsNullOrWhiteSpace(message) ? "OpenAI request failed." : message) {
        ErrorCode = string.IsNullOrWhiteSpace(code) ? null : code;
        ErrorParam = string.IsNullOrWhiteSpace(param) ? null : param;
        StatusCode = statusCode;

        Data["openai:native_transport"] = true;
        Data["openai:status_code"] = (int)statusCode;
        if (!string.IsNullOrWhiteSpace(rawText)) {
            Data["openai:raw"] = Truncate(rawText, 8192);
        }
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

    private static string Truncate(string value, int maxLength) {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength) {
            return value;
        }
        return value.Substring(0, maxLength) + "...(truncated)";
    }
}
