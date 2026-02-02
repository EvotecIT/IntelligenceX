using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a list of thread ids.
/// </summary>
public sealed class ThreadIdListResult {
    /// <summary>
    /// Initializes a new thread id list result.
    /// </summary>
    public ThreadIdListResult(IReadOnlyList<string> data, JsonObject raw, JsonObject? additional) {
        Data = data;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the thread id list.
    /// </summary>
    public IReadOnlyList<string> Data { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a thread id list from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed thread id list result.</returns>
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
