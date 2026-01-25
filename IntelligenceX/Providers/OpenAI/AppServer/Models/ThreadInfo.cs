using System;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

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

    public string Id { get; }
    public string? Preview { get; }
    public string? ModelProvider { get; }
    public DateTimeOffset? CreatedAt { get; }
    public DateTimeOffset? UpdatedAt { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

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
