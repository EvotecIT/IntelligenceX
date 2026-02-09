using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.Tools;

/// <summary>
/// Standard tool output envelope helpers.
/// </summary>
/// <remarks>
/// Tools return a string payload to the model. To keep outputs machine-readable and UI-friendly,
/// tools should return a JSON envelope with consistent top-level fields.
/// </remarks>
public static class ToolOutputEnvelope {
    /// <summary>
    /// Creates a success envelope (<c>ok=true</c>).
    /// </summary>
    /// <param name="data">Optional data payload.</param>
    /// <param name="meta">Optional metadata (paging, truncation, counts, etc.).</param>
    /// <param name="summaryMarkdown">Optional human-readable markdown summary for UI display.</param>
    /// <param name="render">Optional UI render hints (tables, columns, types, etc.).</param>
    public static JsonObject OkObject(JsonObject? data = null, JsonObject? meta = null, string? summaryMarkdown = null, JsonObject? render = null) {
        var obj = new JsonObject().Add("ok", true);
        if (data is not null) {
            obj.Add("data", data);
        }
        if (meta is not null) {
            obj.Add("meta", meta);
        }
        if (!string.IsNullOrWhiteSpace(summaryMarkdown)) {
            obj.Add("summary_markdown", summaryMarkdown);
        }
        if (render is not null) {
            obj.Add("render", render);
        }
        return obj;
    }

    /// <summary>
    /// Serializes a success envelope (<c>ok=true</c>) as JSON.
    /// </summary>
    public static string Ok(JsonObject? data = null, JsonObject? meta = null, string? summaryMarkdown = null, JsonObject? render = null)
        => JsonLite.Serialize(OkObject(data, meta, summaryMarkdown, render));

    /// <summary>
    /// Creates an error envelope (<c>ok=false</c>).
    /// </summary>
    /// <param name="errorCode">Stable, machine-readable error code.</param>
    /// <param name="error">Human-readable error message.</param>
    /// <param name="hints">Optional remediation hints.</param>
    /// <param name="isTransient">Whether the failure is likely transient (retryable).</param>
    public static JsonObject ErrorObject(string errorCode, string error, IEnumerable<string>? hints = null, bool isTransient = false) {
        if (string.IsNullOrWhiteSpace(errorCode)) {
            throw new ArgumentException("Error code cannot be empty.", nameof(errorCode));
        }

        var obj = new JsonObject()
            .Add("ok", false)
            .Add("error_code", errorCode)
            .Add("error", error ?? string.Empty)
            .Add("is_transient", isTransient);

        if (hints is not null) {
            var arr = new JsonArray();
            foreach (var h in hints) {
                if (!string.IsNullOrWhiteSpace(h)) {
                    arr.Add(h);
                }
            }
            if (arr.Count > 0) {
                obj.Add("hints", arr);
            }
        }

        return obj;
    }

    /// <summary>
    /// Serializes an error envelope (<c>ok=false</c>) as JSON.
    /// </summary>
    public static string Error(string errorCode, string error, IEnumerable<string>? hints = null, bool isTransient = false)
        => JsonLite.Serialize(ErrorObject(errorCode, error, hints, isTransient));
}

