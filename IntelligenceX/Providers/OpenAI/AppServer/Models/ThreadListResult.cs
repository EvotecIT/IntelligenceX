using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a paged thread list response.
/// </summary>
/// <example>
/// <code>
/// var list = await client.ListThreadsAsync(limit: 10);
/// foreach (var thread in list.Data) {
///     Console.WriteLine(thread.Id);
/// }
/// </code>
/// </example>
public sealed class ThreadListResult {
    public ThreadListResult(IReadOnlyList<ThreadInfo> data, string? nextCursor, JsonObject raw, JsonObject? additional) {
        Data = data;
        NextCursor = nextCursor;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Threads returned by the service.</summary>
    public IReadOnlyList<ThreadInfo> Data { get; }
    /// <summary>Cursor for the next page, if any.</summary>
    public string? NextCursor { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a thread list from JSON.</summary>
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
