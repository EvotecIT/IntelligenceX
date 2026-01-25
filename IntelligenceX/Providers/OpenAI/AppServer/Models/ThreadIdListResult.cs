using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class ThreadIdListResult {
    public ThreadIdListResult(IReadOnlyList<string> data, JsonObject raw, JsonObject? additional) {
        Data = data;
        Raw = raw;
        Additional = additional;
    }

    public IReadOnlyList<string> Data { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

    public static ThreadIdListResult FromJson(JsonObject obj) {
        var dataArray = obj.GetArray("data") ?? obj.GetArray("items");
        var items = new List<string>();
        if (dataArray is not null) {
            foreach (var entry in dataArray) {
                var value = entry.AsString();
                if (!string.IsNullOrWhiteSpace(value)) {
                    items.Add(value);
                }
            }
        }
        var additional = obj.ExtractAdditional("data", "items");
        return new ThreadIdListResult(items, obj, additional);
    }
}
