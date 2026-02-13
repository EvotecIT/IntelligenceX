using System;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a chat thread summary.
/// </summary>
public sealed class ThreadInfo {
    /// <summary>
    /// Initializes a new thread info model.
    /// </summary>
    public ThreadInfo(string id, string? preview, string? modelProvider, DateTimeOffset? createdAt, DateTimeOffset? updatedAt,
        JsonObject raw, JsonObject? additional, ThreadUsageSummary? usageSummary = null) {
        Id = id;
        Preview = preview;
        ModelProvider = modelProvider;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        Raw = raw;
        Additional = additional;
        UsageSummary = usageSummary;
    }

    /// <summary>
    /// Gets the thread id.
    /// </summary>
    public string Id { get; }
    /// <summary>
    /// Gets the preview text.
    /// </summary>
    public string? Preview { get; }
    /// <summary>
    /// Gets the model provider identifier.
    /// </summary>
    public string? ModelProvider { get; }
    /// <summary>
    /// Gets the creation time (UTC).
    /// </summary>
    public DateTimeOffset? CreatedAt { get; }
    /// <summary>
    /// Gets the last update time (UTC).
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }
    /// <summary>
    /// Gets cumulative usage summary when present.
    /// </summary>
    public ThreadUsageSummary? UsageSummary { get; }

    /// <summary>
    /// Parses a thread info model from JSON.
    /// </summary>
    /// <param name="threadObj">Source JSON object.</param>
    /// <returns>The parsed thread info.</returns>
    public static ThreadInfo FromJson(JsonObject threadObj) {
        var id = threadObj.GetString("id") ?? string.Empty;
        var preview = threadObj.GetString("preview");
        var modelProvider = threadObj.GetString("modelProvider") ?? threadObj.GetString("model");
        var createdAt = threadObj.GetInt64("createdAt");
        var updatedAt = threadObj.GetInt64("updatedAt");
        var usageSummary = ThreadUsageSummary.FromJson(
            threadObj.GetObject("usageSummary")
            ?? threadObj.GetObject("usage_summary")
            ?? threadObj.GetObject("sessionUsage"));
        DateTimeOffset? createdAtValue = createdAt is null ? null : DateTimeOffset.FromUnixTimeSeconds(createdAt.Value);
        DateTimeOffset? updatedAtValue = updatedAt is null ? null : DateTimeOffset.FromUnixTimeSeconds(updatedAt.Value);
        var additional = threadObj.ExtractAdditional(
            "id", "preview", "modelProvider", "model", "createdAt", "updatedAt",
            "usageSummary", "usage_summary", "sessionUsage");
        return new ThreadInfo(id, preview, modelProvider, createdAtValue, updatedAtValue, threadObj, additional, usageSummary);
    }
}

/// <summary>
/// Represents cumulative token usage for a thread/session.
/// </summary>
public sealed class ThreadUsageSummary {
    /// <summary>
    /// Initializes a new usage summary model.
    /// </summary>
    public ThreadUsageSummary(long? inputTokens, long? outputTokens, long? totalTokens, int? turns, JsonObject raw,
        JsonObject? additional) {
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        TotalTokens = totalTokens;
        Turns = turns;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets cumulative input token count.
    /// </summary>
    public long? InputTokens { get; }
    /// <summary>
    /// Gets cumulative output token count.
    /// </summary>
    public long? OutputTokens { get; }
    /// <summary>
    /// Gets cumulative total token count.
    /// </summary>
    public long? TotalTokens { get; }
    /// <summary>
    /// Gets number of turns with usage recorded.
    /// </summary>
    public int? Turns { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses usage summary from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed summary or null.</returns>
    public static ThreadUsageSummary? FromJson(JsonObject? obj) {
        if (obj is null) {
            return null;
        }
        var inputTokens = ReadInt64(obj, "input_tokens", "inputTokens");
        var outputTokens = ReadInt64(obj, "output_tokens", "outputTokens");
        var totalTokens = ReadInt64(obj, "total_tokens", "totalTokens");
        var turnsValue = ReadInt64(obj, "turns", "turn_count", "turnCount");
        var turns = turnsValue.HasValue ? (int?)Math.Max(0, (int)turnsValue.Value) : null;
        var additional = obj.ExtractAdditional(
            "input_tokens", "inputTokens",
            "output_tokens", "outputTokens",
            "total_tokens", "totalTokens",
            "turns", "turn_count", "turnCount");
        return new ThreadUsageSummary(inputTokens, outputTokens, totalTokens, turns, obj, additional);
    }

    /// <summary>
    /// Serializes usage summary to JSON.
    /// </summary>
    public JsonObject ToJson() {
        if (Raw is not null) {
            return Raw;
        }
        var obj = new JsonObject();
        if (InputTokens.HasValue) {
            obj.Add("input_tokens", InputTokens.Value);
        }
        if (OutputTokens.HasValue) {
            obj.Add("output_tokens", OutputTokens.Value);
        }
        if (TotalTokens.HasValue) {
            obj.Add("total_tokens", TotalTokens.Value);
        }
        if (Turns.HasValue) {
            obj.Add("turns", Turns.Value);
        }
        return obj;
    }

    private static long? ReadInt64(JsonObject obj, params string[] keys) {
        foreach (var key in keys) {
            var value = obj.GetInt64(key);
            if (value.HasValue) {
                return value.Value;
            }
            var asDouble = obj.GetDouble(key);
            if (asDouble.HasValue) {
                return (long)Math.Round(asDouble.Value);
            }
        }
        return null;
    }
}
