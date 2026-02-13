using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a single chat turn and its outputs.
/// </summary>
public sealed class TurnInfo {
    /// <summary>
    /// Initializes a new turn info model.
    /// </summary>
    public TurnInfo(string id, string? responseId, string? status, IReadOnlyList<TurnOutput>? outputs, IReadOnlyList<TurnOutput>? imageOutputs,
        JsonObject raw, JsonObject? additional, TurnUsage? usage = null) {
        Id = id;
        ResponseId = responseId;
        Status = status;
        Outputs = outputs ?? Array.Empty<TurnOutput>();
        ImageOutputs = imageOutputs ?? Array.Empty<TurnOutput>();
        Raw = raw;
        Additional = additional;
        Usage = usage;
    }

    /// <summary>
    /// Initializes a new turn info model.
    /// </summary>
    public TurnInfo(string id, string? status, IReadOnlyList<TurnOutput>? outputs, IReadOnlyList<TurnOutput>? imageOutputs,
        JsonObject raw, JsonObject? additional, TurnUsage? usage = null)
        : this(id, null, status, outputs, imageOutputs, raw, additional, usage) { }

    /// <summary>
    /// Gets the turn id.
    /// </summary>
    public string Id { get; }
    /// <summary>
    /// Gets the response id when available.
    /// </summary>
    public string? ResponseId { get; }
    /// <summary>
    /// Gets the turn status.
    /// </summary>
    public string? Status { get; }
    /// <summary>
    /// Gets all outputs.
    /// </summary>
    public IReadOnlyList<TurnOutput> Outputs { get; }
    /// <summary>
    /// Gets image outputs.
    /// </summary>
    public IReadOnlyList<TurnOutput> ImageOutputs { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }
    /// <summary>
    /// Gets token usage details when available.
    /// </summary>
    public TurnUsage? Usage { get; }

    /// <summary>
    /// Parses a turn info model from JSON.
    /// </summary>
    /// <param name="turnObj">Source JSON object.</param>
    /// <returns>The parsed turn info.</returns>
    public static TurnInfo FromJson(JsonObject turnObj) {
        var id = turnObj.GetString("id") ?? string.Empty;
        var status = turnObj.GetString("status");
        var responseId = turnObj.GetString("responseId")
                         ?? turnObj.GetString("response_id")
                         ?? turnObj.GetObject("response")?.GetString("id");
        var usage = TurnUsage.FromJson(
            turnObj.GetObject("usage")
            ?? turnObj.GetObject("token_usage")
            ?? turnObj.GetObject("tokenUsage")
            ?? turnObj.GetObject("response")?.GetObject("usage"));
        var outputs = TurnOutput.FromTurn(turnObj);
        var images = TurnOutput.FilterImages(outputs);
        var additional = turnObj.ExtractAdditional(
            "id", "status", "responseId", "response_id", "output", "outputs", "response", "result",
            "usage", "token_usage", "tokenUsage");
        return new TurnInfo(id, responseId, status, outputs, images, turnObj, additional, usage);
    }
}

/// <summary>
/// Represents token usage details for a single turn.
/// </summary>
public sealed class TurnUsage {
    /// <summary>
    /// Initializes a new turn usage model.
    /// </summary>
    public TurnUsage(long? inputTokens, long? outputTokens, long? totalTokens, long? cachedInputTokens, long? reasoningTokens,
        JsonObject raw, JsonObject? additional) {
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        TotalTokens = totalTokens;
        CachedInputTokens = cachedInputTokens;
        ReasoningTokens = reasoningTokens;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets input/prompt token count.
    /// </summary>
    public long? InputTokens { get; }
    /// <summary>
    /// Gets output/completion token count.
    /// </summary>
    public long? OutputTokens { get; }
    /// <summary>
    /// Gets total token count.
    /// </summary>
    public long? TotalTokens { get; }
    /// <summary>
    /// Gets cached input token count.
    /// </summary>
    public long? CachedInputTokens { get; }
    /// <summary>
    /// Gets reasoning token count.
    /// </summary>
    public long? ReasoningTokens { get; }
    /// <summary>
    /// Gets the raw JSON usage object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses turn usage from JSON.
    /// </summary>
    /// <param name="obj">Source usage object.</param>
    /// <returns>The parsed usage or null.</returns>
    public static TurnUsage? FromJson(JsonObject? obj) {
        if (obj is null) {
            return null;
        }
        var inputTokens = ReadInt64(obj, "input_tokens", "prompt_tokens", "inputTokens", "promptTokens");
        var outputTokens = ReadInt64(obj, "output_tokens", "completion_tokens", "outputTokens", "completionTokens");
        var totalTokens = ReadInt64(obj, "total_tokens", "totalTokens");

        var inputDetails = obj.GetObject("input_tokens_details") ?? obj.GetObject("prompt_tokens_details");
        var outputDetails = obj.GetObject("output_tokens_details") ?? obj.GetObject("completion_tokens_details");
        var cachedInputTokens = ReadInt64(obj, "cached_input_tokens", "cached_tokens")
                                ?? ReadInt64(inputDetails, "cached_tokens", "cachedTokens");
        var reasoningTokens = ReadInt64(obj, "reasoning_tokens", "reasoningTokens")
                              ?? ReadInt64(outputDetails, "reasoning_tokens", "reasoningTokens");

        var additional = obj.ExtractAdditional(
            "input_tokens", "prompt_tokens", "inputTokens", "promptTokens",
            "output_tokens", "completion_tokens", "outputTokens", "completionTokens",
            "total_tokens", "totalTokens",
            "cached_input_tokens", "cached_tokens",
            "reasoning_tokens", "reasoningTokens",
            "input_tokens_details", "prompt_tokens_details",
            "output_tokens_details", "completion_tokens_details");
        return new TurnUsage(inputTokens, outputTokens, totalTokens, cachedInputTokens, reasoningTokens, obj, additional);
    }

    /// <summary>
    /// Serializes usage to JSON.
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
        if (CachedInputTokens.HasValue) {
            obj.Add("cached_input_tokens", CachedInputTokens.Value);
        }
        if (ReasoningTokens.HasValue) {
            obj.Add("reasoning_tokens", ReasoningTokens.Value);
        }
        return obj;
    }

    private static long? ReadInt64(JsonObject? obj, params string[] keys) {
        if (obj is null || keys is null || keys.Length == 0) {
            return null;
        }
        foreach (var key in keys) {
            var value = obj.GetInt64(key);
            if (value.HasValue) {
                return value.Value;
            }
            var asDouble = obj.GetDouble(key);
            if (asDouble.HasValue) {
                return (long)Math.Round(asDouble.Value);
            }
            var asText = obj.GetString(key);
            if (long.TryParse(asText, out var parsed)) {
                return parsed;
            }
        }
        return null;
    }
}
