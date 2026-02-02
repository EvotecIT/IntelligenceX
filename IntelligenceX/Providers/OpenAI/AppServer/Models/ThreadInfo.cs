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
        JsonObject raw, JsonObject? additional) {
        Id = id;
        Preview = preview;
        ModelProvider = modelProvider;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        Raw = raw;
        Additional = additional;
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
        DateTimeOffset? createdAtValue = createdAt is null ? null : DateTimeOffset.FromUnixTimeSeconds(createdAt.Value);
        DateTimeOffset? updatedAtValue = updatedAt is null ? null : DateTimeOffset.FromUnixTimeSeconds(updatedAt.Value);
        var additional = threadObj.ExtractAdditional(
            "id", "preview", "modelProvider", "model", "createdAt", "updatedAt");
        return new ThreadInfo(id, preview, modelProvider, createdAtValue, updatedAtValue, threadObj, additional);
    }
}
