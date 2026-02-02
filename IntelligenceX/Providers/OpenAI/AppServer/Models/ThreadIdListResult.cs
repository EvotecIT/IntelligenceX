using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a list of thread identifiers.
/// </summary>
/// <example>
/// <code>
/// var ids = await client.ListThreadIdsAsync();
/// Console.WriteLine(ids.Data.Count);
/// </code>
/// </example>
public sealed class ThreadIdListResult {
    public ThreadIdListResult(IReadOnlyList<string> data, JsonObject raw, JsonObject? additional) {
        Data = data;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Thread identifiers returned by the service.</summary>
    public IReadOnlyList<string> Data { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a thread id list from JSON.</summary>
    public static ThreadIdListResult FromJson(JsonObject obj) {
        var dataArray = obj.GetArray("data") ?? obj.GetArray("items");
        var items = new List<string>();
        if (dataArray is not null) {
            foreach (var entry in dataArray) {
                var value = entry.AsString();
                if (!string.IsNullOrWhiteSpace(value)) {
                    items.Add(value!);
                }
            }
        }
        var additional = obj.ExtractAdditional("data", "items");
        return new ThreadIdListResult(items, obj, additional);
    }
}
