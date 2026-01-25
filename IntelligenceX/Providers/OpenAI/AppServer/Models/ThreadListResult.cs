using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class ThreadListResult {
    public ThreadListResult(IReadOnlyList<ThreadInfo> data, string? nextCursor, JsonObject raw, JsonObject? additional) {
        Data = data;
        NextCursor = nextCursor;
        Raw = raw;
        Additional = additional;
    }

    public IReadOnlyList<ThreadInfo> Data { get; }
    public string? NextCursor { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

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
