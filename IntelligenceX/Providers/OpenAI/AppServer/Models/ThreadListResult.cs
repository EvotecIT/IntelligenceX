using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a list of chat threads.
/// </summary>
public sealed class ThreadListResult {
    /// <summary>
    /// Initializes a new thread list result.
    /// </summary>
    public ThreadListResult(IReadOnlyList<ThreadInfo> data, string? nextCursor, JsonObject raw, JsonObject? additional) {
        Data = data;
        NextCursor = nextCursor;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the thread list.
    /// </summary>
    public IReadOnlyList<ThreadInfo> Data { get; }
    /// <summary>
    /// Gets the pagination cursor for the next page.
    /// </summary>
    public string? NextCursor { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a thread list from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed thread list result.</returns>
    public static ThreadListResult FromJson(JsonObject obj) {
        var dataArray = obj.GetArray("data") ?? obj.GetArray("items");
        var items = new List<ThreadInfo>();
        if (dataArray is not null) {
            foreach (var entry in dataArray) {
                var threadObj = entry.AsObject();
                if (threadObj is not null) {
                    items.Add(ThreadInfo.FromJson(threadObj));
                }
            }
        }
        var nextCursor = obj.GetString("nextCursor") ?? obj.GetString("next_cursor");
        var additional = obj.ExtractAdditional("data", "items", "nextCursor", "next_cursor");
        return new ThreadListResult(items, nextCursor, obj, additional);
    }
}
