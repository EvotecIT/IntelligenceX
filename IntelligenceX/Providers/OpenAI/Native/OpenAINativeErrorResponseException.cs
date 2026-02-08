using System;
using System.Net;

namespace IntelligenceX.OpenAI.Native;

internal sealed class OpenAINativeErrorResponseException : InvalidOperationException {
    internal OpenAINativeErrorResponseException(string? message, string? code, string? param, HttpStatusCode statusCode)
        : base(string.IsNullOrWhiteSpace(message) ? "OpenAI request failed." : message) {
        ErrorCode = string.IsNullOrWhiteSpace(code) ? null : code;
        ErrorParam = string.IsNullOrWhiteSpace(param) ? null : param;
        StatusCode = statusCode;

        Data["openai:native_transport"] = true;
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
}
