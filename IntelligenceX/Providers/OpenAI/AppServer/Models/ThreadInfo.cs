using System;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a single thread returned by the app-server.
/// </summary>
/// <example>
/// <code>
/// var threads = await client.ListThreadsAsync();
/// var first = threads.Data.Count > 0 ? threads.Data[0] : null;
/// Console.WriteLine(first?.Id);
/// </code>
/// </example>
public sealed class ThreadInfo {
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

    /// <summary>Thread identifier.</summary>
    public string Id { get; }
    /// <summary>Preview text for the thread (if available).</summary>
    public string? Preview { get; }
    /// <summary>Model provider name (if available).</summary>
    public string? ModelProvider { get; }
    /// <summary>Creation timestamp (if provided).</summary>
    public DateTimeOffset? CreatedAt { get; }
    /// <summary>Last update timestamp (if provided).</summary>
    public DateTimeOffset? UpdatedAt { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a thread from JSON.</summary>
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
